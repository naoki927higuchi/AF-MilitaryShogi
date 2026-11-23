using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Web (WebGL) differences, kept in one place (1.6.0). Windows and Android take the same paths as
    /// before; every member here is a no-op or the old behaviour when <see cref="IsWeb"/> is false.
    ///   ・User data: a fixed folder below Unity's IndexedDB mount instead of persistentDataPath
    ///     (which is derived from the page URL and so changes when the host moves the game), and an
    ///     explicit copy to IndexedDB after every write. Nothing depends on the hosting page.
    ///   ・CPU: the browser has no threads for C#, so thinking runs on the main thread.
    ///   ・Font: the browser has no OS fonts; the Web build carries Noto Sans JP.
    ///   ・Play mode only, fixed preset names, no start-up arguments.
    /// </summary>
    public static class WebSupport
    {
        public static bool IsWeb { get { return Application.platform == RuntimePlatform.WebGLPlayer; } }

        /// <summary>Fixed user data folder. "/idbfs" is the mount Unity restores from IndexedDB before the game starts.</summary>
        public const string DataDirectory = "/idbfs/AF-MilitaryShogi";

        /// <summary>Name of the font asset the Web build preloads (Assets/WebFonts, see WebBuild).</summary>
        public const string FontName = "NotoSansJP-Regular";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void AfmsSyncFiles();
#else
        private static void AfmsSyncFiles() { }
#endif

        /// <summary>Web: copies the files just written (presets, settings) to the browser's storage. Elsewhere: nothing.</summary>
        public static void SyncFiles() { if (IsWeb) AfmsSyncFiles(); }

        /// <summary>
        /// Runs one CPU decision. Windows/Android: on a background thread, as before. Web: right here
        /// (a decision takes a few tens of milliseconds). The result and the fault handling are the same.
        /// </summary>
        public static Task<T> Run<T>(Func<T> work)
        {
            if (!IsWeb) return Task.Run(work);
            var done = new TaskCompletionSource<T>();
            try { done.SetResult(work()); }
            catch (Exception e) { done.SetException(e); }
            return done.Task;
        }

        /// <summary>The Japanese font carried by the Web build, or null (not a Web build).</summary>
        public static Font BundledFont()
        {
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                if (f != null && f.name == FontName) return f;
            return null;
        }
    }
}
