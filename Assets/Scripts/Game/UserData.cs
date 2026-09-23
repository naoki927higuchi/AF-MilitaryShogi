using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Files kept between runs: own-army presets and user settings (both plain UTF-8 text) plus a UI
    /// error log. Location: Application.persistentDataPath, or the folder given with -dataDir
    /// (used by the auto-test so real user data is never touched).
    /// </summary>
    public static class UserData
    {
        private static string dir;

        public static string Directory
        {
            get
            {
                if (dir == null)
                {
                    dir = GameBootstrap.Argument("-dataDir") ?? Application.persistentDataPath;
                    System.IO.Directory.CreateDirectory(dir);
                }
                return dir;
            }
        }

        public static string PresetPath { get { return Path.Combine(Directory, "presets.txt"); } }
        public static string SettingsPath { get { return Path.Combine(Directory, "settings.txt"); } }
        public static string ErrorLogPath { get { return Path.Combine(Directory, "ui-errors.log"); } }

        // ------------------------------------------------------------------
        // Presets
        // ------------------------------------------------------------------

        public static FormationPresets LoadPresets()
        {
            try
            {
                if (!File.Exists(PresetPath)) return new FormationPresets();
                var p = FormationPresets.Parse(File.ReadAllText(PresetPath, Encoding.UTF8));
                foreach (var e in p.LoadErrors) Debug.LogWarning("Preset file: " + e);
                return p;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Preset file unreadable: " + e.Message);
                return new FormationPresets();
            }
        }

        public static void SavePresets(FormationPresets presets)
        {
            WriteAtomically(PresetPath, presets.Serialize());
        }

        // ------------------------------------------------------------------
        // Settings (key=value lines)
        // ------------------------------------------------------------------

        public static void LoadSettings(GameSettings s)
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                var map = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(SettingsPath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                string v;
                int i;
                float f;
                if (map.TryGetValue("effect", out v)) s.Effect = v == "Simple" ? EffectMode.Simple : EffectMode.Normal;
                if (map.TryGetValue("speed", out v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f) && (f == 1 || f == 2 || f == 4)) s.EffectSpeed = f;
                if (map.TryGetValue("observationTooltip", out v)) s.ObservationTooltip = v != "0";
                if (map.TryGetValue("sfx", out v)) s.SfxOn = v != "0";
                if (map.TryGetValue("sfxVolume", out v) && int.TryParse(v, out i)) s.SfxVolume = Mathf.Clamp(i, 0, 100);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Settings file unreadable: " + e.Message);
            }
        }

        public static void SaveSettings(GameSettings s)
        {
            var sb = new StringBuilder();
            sb.Append("# AF-MilitaryShogi settings\n");
            sb.Append("effect=").Append(s.Effect).Append('\n');
            sb.Append("speed=").Append(s.EffectSpeed.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("observationTooltip=").Append(s.ObservationTooltip ? 1 : 0).Append('\n');
            sb.Append("sfx=").Append(s.SfxOn ? 1 : 0).Append('\n');
            sb.Append("sfxVolume=").Append(s.SfxVolume).Append('\n');
            WriteAtomically(SettingsPath, sb.ToString());
        }

        private static void WriteAtomically(string path, string text)
        {
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not save " + path + ": " + e.Message);
            }
        }

        public static void AppendErrorLog(string text)
        {
            try { File.AppendAllText(ErrorLogPath, DateTime.Now.ToString("s") + " " + text + "\n", new UTF8Encoding(false)); }
            catch (Exception) { }
        }
    }
}
