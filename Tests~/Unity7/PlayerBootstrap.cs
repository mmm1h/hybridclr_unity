// Copyright (c) 2026 mmm1h. Licensed under the MIT license in the repository root.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HybridCLR;
using UnityEngine;
using UnityEngine.Assemblies;
using UnityEngine.Networking;

public sealed class PlayerBootstrap : MonoBehaviour
{
    [Serializable] private sealed class PayloadConfig { public int version = 1; }
    [Serializable] private sealed class Result
    {
        public string unityVersion, platform, architecture, payloadSha256, aotSha256, error;
        public int expectedVersion, actualVersion;
        public bool passed;
        public string[] checks;
    }
    private static readonly List<string> Checks = new List<string>();
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("Failed: " + name);
        Checks.Add(name);
        Debug.Log("PASS " + name);
    }
    private static string Hash(byte[] bytes)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Run()
    {
        Checks.Clear();
        var host = new GameObject("HybridCLR verification");
        DontDestroyOnLoad(host);
        host.AddComponent<PlayerBootstrap>().StartCoroutine(RunFixture());
    }
    private static IEnumerator ReadStreaming(string name, Action<byte[]> receive)
    {
        string path = Path.Combine(Application.streamingAssetsPath, name);
        if (!path.Contains("://")) { receive(File.ReadAllBytes(path)); yield break; }
        using var request = UnityWebRequest.Get(path);
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success) throw new IOException(path + ": " + request.error);
        receive(request.downloadHandler.data);
    }
    // Catch coroutine I/O failures as well as reflection/runtime failures in one report.
    private static IEnumerator RunFixture()
    {
        var result = new Result { unityVersion = Application.unityVersion, platform = Application.platform.ToString(),
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), expectedVersion = 1 };
        var routine = Execute(result);
        while (true)
        {
            object current;
            try { if (!routine.MoveNext()) break; current = routine.Current; }
            catch (Exception error) { result.error = error.ToString(); Debug.LogException(error); break; }
            yield return current;
        }
        result.checks = Checks.ToArray();
        result.passed = result.error == null && result.actualVersion == result.expectedVersion && Checks.Count >= 17;
        string report = JsonUtility.ToJson(result, true);
        File.WriteAllText(Path.Combine(Application.persistentDataPath, "hybridclr-result.json"), report);
        Debug.Log(report);
        Debug.Log(result.passed ? "HYBRIDCLR_UNITY7_PLAYER_OK" : "HYBRIDCLR_UNITY7_PLAYER_FAILED");
#if !UNITY_IOS || UNITY_EDITOR
        Application.Quit(result.passed ? 0 : 1);
#endif
    }
    private static IEnumerator Execute(Result result)
    {
        string configPath = Path.Combine(Application.persistentDataPath, "payload-config.json");
        if (File.Exists(configPath)) result.expectedVersion = JsonUtility.FromJson<PayloadConfig>(File.ReadAllText(configPath)).version;
        string[] arguments = Environment.GetCommandLineArgs();
        for (int index = 0; index + 1 < arguments.Length; ++index)
            if (arguments[index] == "-hot-version") result.expectedVersion = int.Parse(arguments[index + 1]);
#if !UNITY_EDITOR
        byte[] aot = null;
        var readAot = ReadStreaming(typeof(object).Assembly.GetName().Name + ".dll.bytes", value => aot = value);
        while (readAot.MoveNext()) yield return readAot.Current;
        result.aotSha256 = Hash(aot);
        Check(RuntimeApi.LoadMetadataForAOTAssembly(aot, HomologousImageMode.SuperSet) == LoadImageErrorCode.OK, "player-aot-metadata-supplement");
#endif
        byte[] payload = null;
        string overridePath = Path.Combine(Application.persistentDataPath, "HotPayload.dll.bytes");
        if (File.Exists(overridePath)) payload = File.ReadAllBytes(overridePath);
        else
        {
            var readPayload = ReadStreaming("HotPayload.dll.bytes", value => payload = value);
            while (readPayload.MoveNext()) yield return readPayload.Current;
        }
        result.payloadSha256 = Hash(payload);
        Assembly assembly = CurrentAssemblies.LoadFromBytes(payload);
        Type type = assembly.GetType("Unity7HotUpdateFixture.Calculator", true);
        object instance = Activator.CreateInstance(type);
        result.actualVersion = (int)type.GetMethod("PayloadVersion").Invoke(null, null);
        Check(result.actualVersion == result.expectedVersion, "player-payload-version-" + result.expectedVersion);
        Check((int)type.GetMethod("Sum").Invoke(instance, new object[] { 17, 25 }) == 42, "player-reflection-call");
        Check((int)type.GetMethod("InterfaceDispatch").Invoke(null, new object[] { 8, 13 }) == 21, "player-interface-dispatch");
        Check((int)type.GetMethod("ArrayData").Invoke(null, null) == 12, "player-array-rva");
        Check((int)type.GetMethod("Echo").MakeGenericMethod(typeof(int)).Invoke(null, new object[] { 123 }) == 123, "player-generic-method");
        var adder = (Func<int, int>)type.GetMethod("MakeAdder").Invoke(null, new object[] { 9 });
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(adder(6) == 15, "player-delegate-and-gc");
        Check((bool)type.GetMethod("AtomicOperations").Invoke(null, null), "player-atomics");
        Check((int)type.GetMethod("AotGenericCollection").Invoke(null, null) == 42, "player-aot-generic-collection");
        Check((bool)type.GetField("ContextReady").GetValue(null), "player-module-initializer");
        Check(type.GetCustomAttributes(false).Length == 1, "player-custom-attribute");
        Check(type.GetMethod("MarkedReturn").ReturnParameter.GetCustomAttributes(false).Length == 1, "player-return-attribute");
        Check(type.GetProperty("MarkedProperty").GetCustomAttributes(false).Length == 1, "player-property-attribute");
        var mixed = (Func<float, double, long, double>)type.GetMethod("MixedArguments").CreateDelegate(typeof(Func<float, double, long, double>));
        Check(mixed(1.5f, 2.25, 7) == 10.75, "player-mixed-register-delegate");
        Check((double)type.GetMethod("CallAotDelegate").Invoke(null, new object[] { (Func<float, double, long, double>)((a, b, c) => a + b + c) }) == 10.75,
            "player-interpreter-to-aot-delegate");
        var decimals = (Func<decimal, decimal, decimal>)type.GetMethod("DecimalArithmetic").CreateDelegate(typeof(Func<decimal, decimal, decimal>));
        Check(decimals(1.5m, 2m) == 3.25m, "player-struct-arguments-return");
        Check((string)type.GetMethod("ExceptionMessage").Invoke(null, null) == "hot-exception", "player-hot-exception-handler");
        try { type.GetMethod("ThrowFromHotCode").Invoke(null, null); throw new InvalidOperationException("Missing hot exception"); }
        catch (TargetInvocationException error) { Check(error.InnerException is InvalidOperationException && error.InnerException.Message == "hot-propagated", "player-exception-propagation"); }
    }
}
