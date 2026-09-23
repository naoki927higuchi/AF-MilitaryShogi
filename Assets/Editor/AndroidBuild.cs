using System;
using System.IO;
using MilitaryShogi.Game;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MilitaryShogi.Editor
{
    /// <summary>
    /// Android Release APK (1.3.0): ARM64 / IL2CPP Release, no Development Build, script debugging,
    /// profiler or symbols. Portrait and landscape (auto-rotation). Signed with the project's own local
    /// release key (Build-Android.ps1 passes it through environment variables; it is never stored in the
    /// project). Output: bin/Android/Release-&lt;Version&gt;/AF-MilitaryShogi-&lt;Version&gt;.apk. Not distributed.
    /// </summary>
    public static class AndroidBuild
    {
        public const string PackageName = "com.af.militaryshogi";

        [MenuItem("MilitaryShogi/Build Release APK (Android ARM64)")]
        public static void BuildRelease()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before building.");
            ProjectSetup.Run();
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string version = ProjectSetup.ReadVersion();
            if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$")) throw new ArgumentException("Version must be MAJOR.MINOR.PATCH: " + version);
            if (version != GameBootstrap.Version) throw new InvalidOperationException("VERSION.txt (" + version + ") and GameBootstrap.Version (" + GameBootstrap.Version + ") differ.");
            string output = Path.Combine(root, "bin", "Android", "Release-" + version, "AF-MilitaryShogi-" + version + ".apk");
            bool overwrite = Array.IndexOf(Environment.GetCommandLineArgs(), "-overwriteUnpublished") >= 0;
            if (File.Exists(output) && !overwrite) throw new IOException("APK already exists: " + output);
            string key = Environment.GetEnvironmentVariable("AFMS_KEYSTORE");
            string password = Environment.GetEnvironmentVariable("AFMS_KEY_PASSWORD");
            if (string.IsNullOrEmpty(key) || !File.Exists(key) || string.IsNullOrEmpty(password))
                throw new InvalidOperationException("Release signing key required. Use Build-Android.ps1.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageName);
            var semantic = Version.Parse(version);
            PlayerSettings.Android.bundleVersionCode = checked(semantic.Major * 10000 + semantic.Minor * 100 + semantic.Build);
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.stripEngineCode = true;
            // Portrait and landscape: the game re-lays out on rotation (no restart).
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.useAnimatedAutorotation = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;      // the UI keeps to Screen.safeArea itself
            PlayerSettings.runInBackground = false;                   // Android: the game pauses in the background
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = key;
            PlayerSettings.Android.keystorePass = password;
            PlayerSettings.Android.keyaliasName = "militaryshogi";
            PlayerSettings.Android.keyaliasPass = password;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
#pragma warning disable CS0618
            EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Disabled;
#pragma warning restore CS0618
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ProjectSetup.ScenePath },
                    locationPathName = output,
                    target = BuildTarget.Android,
                    options = BuildOptions.None,
                });
                if (report.summary.result != BuildResult.Succeeded ||
                    (report.summary.options & (BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler)) != 0)
                    throw new Exception("Android Release build failed: " + report.summary.result);
                Debug.Log("RELEASE_APK=" + output);
            }
            finally
            {
                PlayerSettings.Android.keystorePass = "";
                PlayerSettings.Android.keyaliasPass = "";
                PlayerSettings.Android.keystoreName = "";
                PlayerSettings.Android.keyaliasName = "";
                PlayerSettings.Android.useCustomKeystore = false;
                PlayerSettings.runInBackground = true;               // the Windows build keeps running when unfocused
            }
        }
    }
}
