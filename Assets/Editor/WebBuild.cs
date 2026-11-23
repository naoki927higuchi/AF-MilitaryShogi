using System;
using System.IO;
using System.Linq;
using MilitaryShogi.Game;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MilitaryShogi.Editor
{
    /// <summary>
    /// Web (WebGL) build (1.6.0): bin/Web-&lt;Version&gt;/ (index.html, Build/). Gzip, no decompression
    /// fallback (the host must send Content-Encoding: gzip; itch.io and unityroom do).
    ///
    /// The Japanese UI font (Noto Sans JP, SIL OFL 1.1) is added to the player's preloaded assets for
    /// this build only and removed again afterwards, so the Windows and Android builds do not carry it.
    /// </summary>
    public static class WebBuild
    {
        public const string FontPath = "Assets/WebFonts/NotoSansJP-Regular.otf";
        public const string Template = "PROJECT:MilitaryShogi";

        [MenuItem("MilitaryShogi/Build Web (WebGL)")]
        public static void BuildWeb() { Build(false); }

        /// <summary>
        /// Check build: bin/WebDev-&lt;Version&gt;/ with the define AFMS_WEBDEV (keys that open the referee
        /// screens, see WebDevHooks). Never published; the public build does not contain that code.
        /// </summary>
        [MenuItem("MilitaryShogi/Build Web check build (WebDev)")]
        public static void BuildWebDev() { Build(true); }

        private static void Build(bool dev)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before building.");
            ProjectSetup.Run();
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string version = ProjectSetup.ReadVersion();
            if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$")) throw new ArgumentException("Version must be MAJOR.MINOR.PATCH: " + version);
            if (version != GameBootstrap.Version) throw new InvalidOperationException("VERSION.txt (" + version + ") and GameBootstrap.Version (" + GameBootstrap.Version + ") differ.");
            string output = Path.Combine(root, "bin", (dev ? "WebDev-" : "Web-") + version);
            if (!dev && Directory.Exists(output) && Array.IndexOf(Environment.GetCommandLineArgs(), "-overwriteUnpublished") < 0)
                throw new IOException("Output already exists: " + output);

            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) throw new Exception("Font not found: " + FontPath);
            if (font.name != WebSupport.FontName) throw new Exception("Font asset name is " + font.name + ", expected " + WebSupport.FontName);

            PlayerSettings.WebGL.template = Template;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = false;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;

            var before = PlayerSettings.GetPreloadedAssets();
            try
            {
                PlayerSettings.SetPreloadedAssets(before.Where(a => a != null && a != font).Concat(new UnityEngine.Object[] { font }).ToArray());
                Directory.CreateDirectory(output);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ProjectSetup.ScenePath },
                    locationPathName = output,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None,
                    extraScriptingDefines = dev ? new[] { "AFMS_WEBDEV" } : new string[0],
                });
                if (report.summary.result != BuildResult.Succeeded) throw new Exception("Web build failed: " + report.summary.result);
            }
            finally
            {
                PlayerSettings.SetPreloadedAssets(before.Where(a => a != font).ToArray());
                AssetDatabase.SaveAssets();
            }
            Debug.Log("WEB_BUILD=" + output);
        }
    }
}
