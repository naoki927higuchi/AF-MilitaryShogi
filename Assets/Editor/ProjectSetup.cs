using System;
using System.IO;
using MilitaryShogi.Game;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MilitaryShogi.Editor
{
    /// <summary>
    /// Reproducible project setup: material templates, the main scene and player settings are
    /// generated from code, so nothing has to be clicked together by hand.
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        private const string MaterialDir = "Assets/Generated/Resources/Materials";

        [MenuItem("MilitaryShogi/Setup Project (materials, scene, settings)")]
        public static void Run()
        {
            EnsureMaterials();
            EnsureScene();
            ApplyPlayerSettings(ReadVersion());
            AssetDatabase.SaveAssets();
            Debug.Log("ProjectSetup done");
        }

        public static string ReadVersion()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return File.ReadAllText(Path.Combine(root, "VERSION.txt")).Trim();
        }

        private static void EnsureMaterials()
        {
            Directory.CreateDirectory(MaterialDir);
            CreateMaterial(MaterialDir + "/Lit.mat", "Standard");
            CreateMaterial(MaterialDir + "/Overlay.mat", "Sprites/Default");
        }

        private static void CreateMaterial(string path, string shaderName)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new Exception("Shader not found: " + shaderName);
            AssetDatabase.CreateAsset(new Material(shader), path);
        }

        private static void EnsureScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Bootstrap").AddComponent<GameBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void ApplyPlayerSettings(string version)
        {
            PlayerSettings.companyName = "AF";
            PlayerSettings.productName = "AF-MilitaryShogi";
            PlayerSettings.bundleVersion = version;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        }
    }

    /// <summary>Import settings for everything the texture generator writes.</summary>
    public sealed class GeneratedTextureImport : AssetPostprocessor
    {
        // Bump when the settings below change so Unity re-imports the textures.
        public override uint GetVersion() { return 3; }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Generated/Resources/Textures/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.npotScale = TextureImporterNPOTScale.ToNearest;   // POT so block compression applies (UVs are normalized)
            ti.anisoLevel = 8;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            bool tiled = assetPath.Contains("board_wood") || assetPath.Contains("table_wood") || assetPath.Contains("piece_side");
            ti.wrapMode = tiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.maxTextureSize = assetPath.Contains("/Board/") ? 2048 : 512;
        }
    }

    public static class WindowsBuild
    {
        [MenuItem("MilitaryShogi/Build Release (Windows x64)")]
        public static void BuildRelease()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before building.");
            ProjectSetup.Run();
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string version = Argument("-releaseVersion") ?? ProjectSetup.ReadVersion();
            if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$")) throw new ArgumentException("Version must be MAJOR.MINOR.PATCH: " + version);
            if (version != GameBootstrap.Version) throw new InvalidOperationException("VERSION.txt (" + version + ") and GameBootstrap.Version (" + GameBootstrap.Version + ") differ.");
            string output = Path.Combine(root, "bin", "Release-" + version);
            if (Directory.Exists(output) && Argument("-overwriteUnpublished") == null)
                throw new IOException("Output already exists: " + output);
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = Path.Combine(output, "AF-MilitaryShogi.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Windows build failed: " + report.summary.result);
            Debug.Log("RELEASE_EXE=" + Path.Combine(output, "AF-MilitaryShogi.exe"));
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == name) return i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : "";
            return null;
        }
    }
}
