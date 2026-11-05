using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Unscrew.Editor
{
    /// <summary>
    /// Standard Android build (skill `android-build`): applies the project player settings, builds
    /// Assets/Scenes/Main.unity into Builds/Unscrew_&lt;version&gt;.apk. Menu: Unscrew → Build Android APK.
    /// </summary>
    public static class BuildScript
    {
        public const string ApkDir = "Builds";
        public const string MainScene = "Assets/Scenes/Main.unity";
        public const string PackageName = "com.novatest.unscrew";

        [MenuItem("Unscrew/Build Android APK")]
        public static void BuildAndroid()
        {
            ApplyAndroidSettings();
            Directory.CreateDirectory(ApkDir);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { MainScene },
                locationPathName = $"{ApkDir}/Unscrew_{PlayerSettings.bundleVersion}.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            long mb = (long)(report.summary.totalSize / (1024UL * 1024UL));
            if (report.summary.result == BuildResult.Succeeded)
                UnityEngine.Debug.Log($"[BuildScript] Build succeeded: {report.summary.outputPath} ({mb} MB, {report.summary.totalErrors} errors)");
            else
                UnityEngine.Debug.LogError($"[BuildScript] Build failed: {report.summary.result}, {report.summary.totalErrors} errors");
        }

        /// <summary>Package/product/portrait, min API 24, IL2CPP + ARM64, .NET Standard 2.1, debug signing.</summary>
        public static void ApplyAndroidSettings()
        {
            PlayerSettings.companyName = "NovaTest";
            PlayerSettings.productName = "Unscrew 3D";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageName);
            PlayerSettings.bundleVersion = GitCommitCount();
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            if (EditorBuildSettings.scenes.Length == 0 || System.Array.Find(EditorBuildSettings.scenes, s => s.path == MainScene) == null)
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScene, true) };
        }

        /// <summary>bundle version = number of commits (git rev-list --count HEAD); 1.0 on any failure.</summary>
        static string GitCommitCount()
        {
            try
            {
                var psi = new ProcessStartInfo("git", "rev-list --count HEAD")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(10000);
                    return p.ExitCode == 0 && output.Length > 0 ? output : "1.0";
                }
            }
            catch
            {
                return "1.0";
            }
        }
    }
}
