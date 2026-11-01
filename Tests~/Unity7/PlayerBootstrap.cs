// Copyright (c) 2026 mmm1h. Licensed under the MIT license in the repository root.
using System;
using System.IO;
using System.Reflection;
using HybridCLR;
using UnityEngine;
using UnityEngine.Assemblies;

public static class PlayerBootstrap
{
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("Failed: " + name);
        Debug.Log("PASS " + name);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Run()
    {
        try
        {
#if !UNITY_EDITOR
            string aotPath = Path.Combine(Application.streamingAssetsPath, typeof(object).Assembly.GetName().Name + ".dll.bytes");
            Check(RuntimeApi.LoadMetadataForAOTAssembly(File.ReadAllBytes(aotPath), HomologousImageMode.SuperSet) == LoadImageErrorCode.OK,
                "player-aot-metadata-supplement");
#endif
            string payloadPath = Path.Combine(Application.streamingAssetsPath, "HotPayload.dll.bytes");
            Assembly assembly = CurrentAssemblies.LoadFromBytes(File.ReadAllBytes(payloadPath));
            Type type = assembly.GetType("Unity7HotUpdateFixture.Calculator", true);
            object instance = Activator.CreateInstance(type);
            string[] arguments = Environment.GetCommandLineArgs();
            int expectedVersion = 1;
            for (int index = 0; index + 1 < arguments.Length; ++index)
                if (arguments[index] == "-hot-version") expectedVersion = int.Parse(arguments[index + 1]);
            Check((int)type.GetMethod("PayloadVersion").Invoke(null, null) == expectedVersion, "player-payload-version-" + expectedVersion);
            Check((int)type.GetMethod("Sum").Invoke(instance, new object[] { 17, 25 }) == 42, "player-reflection-call");
            Check((int)type.GetMethod("InterfaceDispatch").Invoke(null, new object[] { 8, 13 }) == 21, "player-interface-dispatch");
            Check((int)type.GetMethod("ArrayData").Invoke(null, null) == 12, "player-array-rva");
            MethodInfo generic = type.GetMethod("Echo").MakeGenericMethod(typeof(int));
            Check((int)generic.Invoke(null, new object[] { 123 }) == 123, "player-generic-method");
            var adder = (Func<int, int>)type.GetMethod("MakeAdder").Invoke(null, new object[] { 9 });
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(adder(6) == 15, "player-delegate-and-gc");
            Check((bool)type.GetMethod("AtomicOperations").Invoke(null, null), "player-atomics");
            Check((int)type.GetMethod("AotGenericCollection").Invoke(null, null) == 42, "player-aot-generic-collection");
            Check((bool)type.GetField("ContextReady").GetValue(null), "player-module-initializer");
            Check(type.GetCustomAttributes(false).Length == 1, "player-custom-attribute");
            Check(type.GetMethod("MarkedReturn").ReturnParameter.GetCustomAttributes(false).Length == 1, "player-return-attribute");
            Check(type.GetProperty("MarkedProperty").GetCustomAttributes(false).Length == 1, "player-property-attribute");
            Debug.Log("HYBRIDCLR_UNITY7_PLAYER_OK");
            Application.Quit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            Application.Quit(1);
        }
    }
}
