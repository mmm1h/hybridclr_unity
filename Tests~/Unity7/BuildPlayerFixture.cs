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

    private static void BuildWithApi(ApiCompatibilityLevel api)
    {
        string project = Directory.GetParent(Application.dataPath).FullName;
        PlayerSettings.productName = "HybridCLRUnity7Fixture";
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, api);
        EditorUserBuildSettings.development = false;
        var settings = HybridCLRSettings.instance;
        settings.enable = true;
        settings.useGlobalIl2cpp = false;
        settings.hotUpdateAssemblies = new[] { "HotPayload" };
        settings.externalHotUpdateAssembliyDirs = new[] {
            Path.Combine(project, "ExternalHotUpdate"),
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
        string stripped = HybridCLR.SettingsUtil.GetAssembliesPostIl2CppStripDir(BuildTarget.StandaloneWindows64);
        string corelibName = File.Exists(Path.Combine(stripped, "System.Private.CoreLib.dll")) ? "System.Private.CoreLib.dll" : "mscorlib.dll";
        File.Copy(Path.Combine(stripped, corelibName), Path.Combine(Application.streamingAssetsPath, corelibName + ".bytes"), true);
        AssetDatabase.Refresh();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { scenePath }, target = BuildTarget.StandaloneWindows64,
            locationPathName = Path.Combine(project, "Build/HybridCLRUnity7Fixture.exe"), options = BuildOptions.None });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Unity 7 fixture Player build failed: " + report.summary.result);
        Debug.Log("HYBRIDCLR_UNITY7_PLAYER_BUILD_OK");
    }
}
