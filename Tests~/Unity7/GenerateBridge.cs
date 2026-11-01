// Copyright (c) 2026 mmm1h. Licensed under the MIT license in the repository root.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using dnlib.DotNet;
using HybridCLR.CodeGen.MethodBridge;
using HybridCLR.Meta;

internal static class GenerateBridge
{
    public static void Main(string[] args)
    {
        if (args.Length < 1) throw new ArgumentException("Pass MethodBridge.inc, optionally followed by a hot DLL and reference directories");
        var methods = new List<GenericMethod>();
        var resolver = new AssemblyResolver();
        var context = new ModuleContext(resolver);
        resolver.DefaultModuleContext = context;
        if (args.Length > 1) resolver.PreSearchPaths.Add(Path.GetDirectoryName(Path.GetFullPath(args[1])));
        foreach (string directory in args.Skip(2)) resolver.PreSearchPaths.Add(directory);
        using var module = args.Length > 1 ? ModuleDefMD.Load(args[1], context) : null;
        if (args.Length > 1)
        {
            var referencedTypes = module.GetTypeRefs().Select(reference => reference.ResolveTypeDefThrow()).Distinct().ToList();
            var root = new XElement("linker", new XElement("assembly", new XAttribute("fullname", "AotHost"), new XAttribute("preserve", "all")));
            foreach (var group in referencedTypes.GroupBy(type => type.Module.Assembly.Name.String))
                root.Add(new XElement("assembly", new XAttribute("fullname", group.Key), group.Select(type =>
                    new XElement("type", new XAttribute("fullname", type.FullName), new XAttribute("preserve", "all")))));
            new XDocument(root).Save(Path.ChangeExtension(args[0], ".link.xml"));
            var definitions = new HashSet<MethodDef>(module.GetTypes().SelectMany(type => type.Methods));
            foreach (MemberRef reference in module.GetMemberRefs())
            {
                if (!reference.IsMethodRef) continue;
                MethodDef definition = reference.ResolveMethod();
                if (definition != null) definitions.Add(definition);
                if (definition != null && reference.Class is TypeSpec spec && spec.TypeSig is GenericInstSig instance)
                {
                    var delegateMethods = definition.DeclaringType.IsDelegate ? definition.DeclaringType.Methods : new[] { definition }.AsEnumerable();
                    foreach (var referenced in delegateMethods)
                        methods.Add(new GenericMethod(referenced, instance.GenericArguments.ToList(), null));
                }
            }
            foreach (MethodDef method in definitions)
            {
                if (method.DeclaringType.HasGenericParameters) continue;
                if (!method.HasGenericParameters) methods.Add(new GenericMethod(method, null, null));
                else if (method.Name == "Echo" && method.GenericParameters.Count == 1)
                    methods.Add(new GenericMethod(method, null, new List<TypeSig> { module.CorLibTypes.Int32 }));
            }
        }
        new Generator(new Generator.Options
        {
            OutputFile = args[0],
            GenericMethods = methods,
            ReversePInvokeMethods = new List<RawMonoPInvokeCallbackMethodInfo>(),
            CalliMethodSignatures = new List<CallNativeMethodSignatureInfo>(),
            Development = false,
            Log = Console.WriteLine,
            LogError = Console.Error.WriteLine,
        }).Generate();
    }
}
