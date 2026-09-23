using System;
using System.Collections.Generic;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Start-up arguments. PC: the command line. Android: the launch intent's string extra "args"
    /// (e.g. <c>adb shell am start -n …/com.unity3d.player.UnityPlayerGameActivity -e args "-remote"</c>),
    /// split on spaces. Only the test harnesses use arguments; a normal launch has none.
    /// </summary>
    public static class LaunchArgs
    {
        private static string[] all;

        public static string[] All
        {
            get
            {
                if (all != null) return all;
                var list = new List<string>(Environment.GetCommandLineArgs());
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                    {
                        string extra = intent.Call<string>("getStringExtra", "args");
                        if (!string.IsNullOrEmpty(extra)) list.AddRange(extra.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                    }
                }
                catch (Exception e) { Debug.LogWarning("Launch intent not readable: " + e.Message); }
#endif
                all = list.ToArray();
                return all;
            }
        }

        public static bool Has(string flag) { return Array.IndexOf(All, flag) >= 0; }

        /// <summary>Value after <paramref name="name"/>, or null.</summary>
        public static string Value(string name)
        {
            var args = All;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
