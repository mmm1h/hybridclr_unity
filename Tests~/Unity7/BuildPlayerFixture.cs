// Copyright (c) 2026 mmm1h. Licensed under the MIT license in the repository root.
using System;
using System.IO;
using HybridCLR.Commands;
using HybridCLR.Installer;
using HybridCLR.Settings;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildPlayerFixture
{
    private const string PlayPending = "HybridCLR.Unity7.Play.Pending";
    private const string PlayPassed = "HybridCLR.Unity7.Play.Passed";
    private const string PlayStarted = "HybridCLR.Unity7.Play.Started";

    public static void Reinstall()
    {
        new InstallerController().InstallDefaultHybridCLR();
        string runtime = Path.Combine(HybridCLR.SettingsUtil.LocalIl2CppDir, "libil2cpp/il2cpp-class-internals.h");
        if (!File.ReadAllText(runtime).Contains("methodPointerCallByInterp"))
            throw new InvalidOperationException("The runtime source patches were not applied");
        Debug.Log("HYBRIDCLR_UNITY7_NESTED_INSTALL_OK");
    }

    public static void Play()
    {
        SessionState.SetBool(PlayPending, true);
        SessionState.SetBool(PlayPassed, false);
        SessionState.SetString(PlayStarted, DateTime.UtcNow.Ticks.ToString());
        ResumePlayTest();
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void ResumePlayTest()
    {
        if (!SessionState.GetBool(PlayPending, false)) return;
        DetachPlayTest();
        Application.logMessageReceived += OnPlayLog;
        EditorApplication.playModeStateChanged += OnPlayState;
        EditorApplication.update += OnPlayUpdate;
        AssemblyReloadEvents.beforeAssemblyReload += DetachPlayTest;
        Debug.Log("HYBRIDCLR_UNITY7_EDITOR_TEST_ATTACHED");
    }

    private static void DetachPlayTest()
    {
        Application.logMessageReceived -= OnPlayLog;
        EditorApplication.playModeStateChanged -= OnPlayState;
        EditorApplication.update -= OnPlayUpdate;
        AssemblyReloadEvents.beforeAssemblyReload -= DetachPlayTest;
    }

    private static void OnPlayLog(string message, string trace, LogType type)
    {
        if (message == "HYBRIDCLR_UNITY7_PLAYER_OK") SessionState.SetBool(PlayPassed, true);
        if ((type == LogType.Exception || type == LogType.Error)
            && (trace.Contains("PlayerBootstrap") || trace.Contains("HybridCLR")))
        {
            SessionState.SetBool(PlayPending, false);
            DetachPlayTest();
            EditorApplication.Exit(1);
        }
    }

    private static void OnPlayState(PlayModeStateChange state)
    {
        Debug.Log("HYBRIDCLR_UNITY7_EDITOR_STATE=" + state);
        if (state != PlayModeStateChange.EnteredEditMode) return;
        bool passed = SessionState.GetBool(PlayPassed, false);
        SessionState.SetBool(PlayPending, false);
        DetachPlayTest();
        Debug.Log("HYBRIDCLR_UNITY7_EDITOR_PLAY_EXIT=" + passed);
        EditorApplication.Exit(passed ? 0 : 1);
    }

    private static void OnPlayUpdate()
    {
        if (SessionState.GetBool(PlayPassed, false) && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        long ticks = long.Parse(SessionState.GetString(PlayStarted, "0"));
        if (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) > TimeSpan.FromSeconds(90)) EditorApplication.Exit(2);
    }

    public static void Build()
    {
#pragma warning disable CS0618
        BuildWithApi(ApiCompatibilityLevel.NET);
#pragma warning restore CS0618
    }

    public static void BuildDefaultApi() { BuildWithApi(ApiCompatibilityLevel.NET_Standard); }
    public static void BuildAndroid() { BuildWithApi(ApiCompatibilityLevel.NET_Standard, BuildTarget.Android); }
    public static void BuildIosSimulator() { BuildWithApi(ApiCompatibilityLevel.NET_Standard, BuildTarget.iOS); }

    private static void BuildWithApi(ApiCompatibilityLevel api, BuildTarget target = BuildTarget.StandaloneWindows64)
    {
        string project = Directory.GetParent(Application.dataPath).FullName;
        PlayerSettings.productName = "HybridCLRUnity7Fixture";
        NamedBuildTarget namedTarget = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
        PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetApiCompatibilityLevel(namedTarget, api);
        PlayerSettings.SetApplicationIdentifier(namedTarget, "com.mmm1h.hybridclr.unity7fixture");
        EditorUserBuildSettings.development = target == BuildTarget.Android;
        if (target == BuildTarget.Android)
        {
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            PlayerSettings.SetGraphicsAPIs(target, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
        }
        if (target == BuildTarget.iOS)
        {
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
            PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;
            PlayerSettings.iOS.targetOSVersionString = "15.0";
        }
        var settings = HybridCLRSettings.instance;
        settings.enable = true;
        settings.useGlobalIl2cpp = false;
        settings.hotUpdateAssemblies = new[] { "HotPayload" };
        settings.externalHotUpdateAssembliyDirs = new[] {
            Path.Combine(project, "ExternalHotUpdate"),
            Environment.GetEnvironmentVariable("HYBRIDCLR_FIXTURE_BCL") ??
                Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/windowsstandalonesupport/il2cpp/unityaot/x64/lib") };
        HybridCLRSettings.Save();
        string scenePath = "Assets/Empty.unity";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, scenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
        AssetDatabase.SaveAssets();
        var installer = new InstallerController();
        if (!installer.HasInstalledHybridCLR())
            installer.InstallDefaultHybridCLR();
        PrebuildCommand.GenerateAll();
        string stripped = HybridCLR.SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
        string corelibName = File.Exists(Path.Combine(stripped, "System.Private.CoreLib.dll")) ? "System.Private.CoreLib.dll" : "mscorlib.dll";
        File.Copy(Path.Combine(stripped, corelibName), Path.Combine(Application.streamingAssetsPath, corelibName + ".bytes"), true);
        AssetDatabase.Refresh();
        string outputName = target == BuildTarget.Android ? "HybridCLRUnity7Fixture.apk" :
            target == BuildTarget.iOS ? "iOS" : "HybridCLRUnity7Fixture.exe";
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { scenePath }, target = target,
            locationPathName = Path.Combine(project, "Build", outputName),
            options = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Unity 7 fixture Player build failed: " + report.summary.result);
        Debug.Log("HYBRIDCLR_UNITY7_PLAYER_BUILD_OK");
    }
}
