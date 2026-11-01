// Copyright (c) 2026 mmm1h. Licensed under the MIT license in the repository root.
#if UNITY_7000_0_OR_NEWER
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HybridCLR.Utils;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HybridCLR.Installer
{
    internal static class Unity7RuntimeInstaller
    {
        [Serializable]
        internal class Pin
        {
            public string unity_version;
            public string revision;
            public string source_tree_sha256;
            public int source_file_count;
            public int installation_layout;
            public string hybridclr_url;
            public string hybridclr_revision;
            public string il2cpp_plus_url;
            public string il2cpp_plus_revision;
        }

        private const string ReceiptName = "unity7-runtime.json";

        internal static bool HasInstalled()
        {
            Pin pin = LoadPin();
            string receiptPath = Path.Combine(SettingsUtil.LocalIl2CppDir, ReceiptName);
            if (!File.Exists(receiptPath)) return false;
            Pin installed = JsonUtility.FromJson<Pin>(File.ReadAllText(receiptPath));
            return installed != null && installed.unity_version == pin.unity_version && installed.revision == pin.revision
                && installed.source_tree_sha256 == pin.source_tree_sha256 && installed.hybridclr_revision == pin.hybridclr_revision
                && installed.il2cpp_plus_revision == pin.il2cpp_plus_revision && installed.installation_layout == pin.installation_layout
                && File.Exists(Path.Combine(SettingsUtil.LocalIl2CppDir, "il2cpp/bin/Release/net10.0/il2cpp.exe"))
                && File.Exists(Path.Combine(SettingsUtil.LocalIl2CppDir, "Unity.IL2CPP.Bee.BuildLogic/bin/Release/net10.0/Unity.IL2CPP.Bee.BuildLogic.dll"));
        }

        internal static Pin LoadPin()
        {
            string path = $"{SettingsUtil.ProjectDir}/{SettingsUtil.HybridCLRDataPathInPackage}/unity7_runtime.json";
            var pin = JsonUtility.FromJson<Pin>(File.ReadAllText(path));
            if (pin == null || Application.unityVersion != pin.unity_version)
                throw new NotSupportedException($"This HybridCLR port supports only Unity {pin?.unity_version}; current editor is {Application.unityVersion}");
            if (Application.platform != RuntimePlatform.WindowsEditor)
                throw new NotSupportedException("This Unity 7 maintenance installer currently supports Windows Editor only");
            return pin;
        }

        internal static void ValidateInstallation()
        {
            Pin pin = LoadPin();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new NotSupportedException("This Unity 7 maintenance runtime currently permits only Win64 IL2CPP builds");
            if (SettingsUtil.HybridCLRSettings.useGlobalIl2cpp)
                throw new NotSupportedException("Unity 7 requires the project-local patched IL2CPP SDK");
            if (!HasInstalled())
                throw new InvalidOperationException("The installed Unity 7 runtime does not match the package lock; reinstall it");
        }

        private static void PrepareDevelopmentLayout(string sdk)
        {
            // UNITY_IL2CPP_PATH intentionally selects Unity's development layout.
            // Supply the matching source layout for the editor's tool discovery.
            string deploy = Path.Combine(sdk, "build", "deploy");
            foreach (string executable in Directory.GetFiles(deploy, "*.exe"))
            {
                string destination = Path.Combine(sdk, Path.GetFileNameWithoutExtension(executable), "bin", "Release", "net10.0");
                Directory.CreateDirectory(destination);
                foreach (string file in Directory.GetFiles(deploy))
                    File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }
            foreach (string library in new[] { "Unity.Options", "Unity.Linker.Api", "Unity.IL2CPP.Api", "Unity.Api.Attributes",
                "Unity.IL2CPP.Bee.IL2CPPExeCompileCppBuildProgram.Data", "Unity.IL2CPP.Bee.BuildLogic" })
            {
                string projectRoot = library == "Unity.Options" ? Path.Combine(sdk, "external", "UnityOptions") : sdk;
                string tfm = library == "Unity.IL2CPP.Bee.BuildLogic" ? "net10.0" : "netstandard2.0";
                string destination = Path.Combine(projectRoot, library, "bin", "Release", tfm);
                Directory.CreateDirectory(destination);
                File.Copy(Path.Combine(deploy, library + ".dll"), Path.Combine(destination, library + ".dll"));
            }
            CopyTree(Path.Combine(sdk, "BCLExtensions"), Path.Combine(sdk, "build", "tests", "BCLExtensions", "net471"));
        }

        private static void RunGit(string directory, params string[] arguments)
        {
            using var process = new Process();
            process.StartInfo.FileName = "git";
            process.StartInfo.WorkingDirectory = directory;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            // Source preparation must not discover the surrounding game repository.
            process.StartInfo.EnvironmentVariables["GIT_CEILING_DIRECTORIES"] = directory;
            process.StartInfo.Arguments = string.Join(" ", arguments.Select(QuoteArgument));
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            string output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(" ", arguments)} failed ({process.ExitCode}): {output}");
            if (!string.IsNullOrWhiteSpace(output)) Debug.Log(output);
        }

        private static string QuoteArgument(string argument)
        {
            var quoted = new StringBuilder("\"");
            int slashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\') { ++slashes; continue; }
                quoted.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                quoted.Append(character);
                slashes = 0;
            }
            return quoted.Append('\\', slashes * 2).Append('"').ToString();
        }

        private static string RelativePath(string root, string file)
        {
            return file.Substring(root.TrimEnd('\\', '/').Length + 1).Replace('\\', '/');
        }

        private static void Checkout(string directory, string url, string revision)
        {
            Directory.CreateDirectory(directory);
            RunGit(directory, "init", "--quiet");
            RunGit(directory, "fetch", "--quiet", "--depth=1", url, revision);
            RunGit(directory, "checkout", "--quiet", "--detach", "FETCH_HEAD");
        }

        private static void CopyTree(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.EnumerateDirectories(source))
                CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
            foreach (string file in Directory.EnumerateFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        private static void ValidateSource(string libil2cpp, Pin pin)
        {
            string[] files = Directory.GetFiles(libil2cpp, "*", SearchOption.AllDirectories)
                .OrderBy(file => RelativePath(libil2cpp, file), StringComparer.Ordinal).ToArray();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string file in files)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(RelativePath(libil2cpp, file) + "\0"));
                using var stream = File.OpenRead(file);
                using var fileHash = SHA256.Create();
                hash.AppendData(fileHash.ComputeHash(stream));
            }
            string actual = BitConverter.ToString(hash.GetHashAndReset()).Replace("-", "").ToLowerInvariant();
            if (files.Length != pin.source_file_count || actual != pin.source_tree_sha256)
                throw new InvalidOperationException($"IL2CPP source does not match Unity {pin.unity_version} ({pin.revision}); refusing to patch it");
        }

        internal static void Install(string editorIl2cpp)
        {
            Pin pin = LoadPin();
            ValidateSource(Path.Combine(editorIl2cpp, "libil2cpp"), pin);
            Directory.CreateDirectory(SettingsUtil.HybridCLRDataDir);
            string staging = Path.Combine(SettingsUtil.HybridCLRDataDir, "Unity7Install-" + Guid.NewGuid().ToString("N"));
            string data = Path.Combine(staging, "data");
            string sdk = Path.Combine(data, "il2cpp");
            string backup = SettingsUtil.LocalUnityDataDir + ".previous-" + Guid.NewGuid().ToString("N");
            bool published = false;
            try
            {
                Checkout(Path.Combine(staging, "hybridclr"), pin.hybridclr_url, pin.hybridclr_revision);
                Checkout(Path.Combine(staging, "il2cpp_plus"), pin.il2cpp_plus_url, pin.il2cpp_plus_revision);
                CopyTree(editorIl2cpp, sdk);
                PrepareDevelopmentLayout(sdk);
                string patches = Path.Combine(staging, "il2cpp_plus", "unity7", pin.unity_version);
                foreach (string patch in Directory.GetFiles(patches, "*.patch").OrderBy(path => path, StringComparer.Ordinal))
                {
                    RunGit(sdk, "apply", "--check", "--unidiff-zero", patch);
                    RunGit(sdk, "apply", "--unidiff-zero", patch);
                }
                CopyTree(Path.Combine(staging, "hybridclr", "hybridclr"), Path.Combine(sdk, "libil2cpp", "hybridclr"));
                File.Copy(Path.Combine(staging, "hybridclr", "LICENSE"), Path.Combine(sdk, "libil2cpp", "hybridclr", "LICENSE"));
                File.Copy(Path.Combine(staging, "il2cpp_plus", "LICENSE"), Path.Combine(sdk, "libil2cpp", "hybridclr", "il2cpp_plus-LICENSE"));
                string generated = Path.Combine(sdk, "libil2cpp", "hybridclr", "generated");
                Directory.CreateDirectory(generated);
                File.WriteAllText(Path.Combine(generated, "UnityVersion.inc"), "#define HYBRIDCLR_UNITY_VERSION 70000000\n#define HYBRIDCLR_UNITY7_EXPERIMENTAL 1\n");
                File.WriteAllText(Path.Combine(generated, "AssemblyManifest.inc"), "");
                File.WriteAllText(Path.Combine(sdk, ReceiptName), JsonUtility.ToJson(pin, true));
                if (Directory.Exists(SettingsUtil.LocalUnityDataDir)) Directory.Move(SettingsUtil.LocalUnityDataDir, backup);
                Directory.Move(data, SettingsUtil.LocalUnityDataDir);
                published = true;
                Debug.Log($"Installed pinned Unity {pin.unity_version} HybridCLR sources into the project-local SDK");
            }
            finally
            {
                if (!published && Directory.Exists(backup) && !Directory.Exists(SettingsUtil.LocalUnityDataDir))
                    Directory.Move(backup, SettingsUtil.LocalUnityDataDir);
                if (published && Directory.Exists(backup)) DirectoryUtil.RemoveDir(backup);
                if (Directory.Exists(staging)) DirectoryUtil.RemoveDir(staging);
            }
        }
    }
}
#endif
