using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Sound diagnosis in the built player (-audioprobe REPORT_FILE). Unlike the auto-test's timing
    /// checks it measures what Unity actually mixes for output (AudioListener.GetOutputData), so a
    /// missing listener, a silent source or unloaded clips show up as zero signal:
    ///  1. runtime state of listener / source / clips / audio configuration;
    ///  2. direct AudioSource.PlayOneShot(sfx_select), bypassing game events;
    ///  3. the game path (clicking an own piece in setup → AudioDirector.Play(Select));
    ///  4. 効果音 OFF (must be silent) and volume 100% vs 50% (peak must scale).
    /// Writes the report and quits (exit 0 = audible output measured where expected).
    /// </summary>
    public sealed class AudioProbe : MonoBehaviour
    {
        private GameController game;
        private string reportPath;
        private readonly StringBuilder sb = new StringBuilder();
        private readonly float[] buffer = new float[1024];
        private bool pass = true;

        public void Begin(GameController controller, string path)
        {
            game = controller;
            reportPath = path;
            StartCoroutine(Run());
        }

        private void Check(bool ok, string what)
        {
            if (!ok) pass = false;
            sb.AppendLine((ok ? "OK   " : "FAIL ") + what);
        }

        /// <summary>Peak of the final mix (both channels) over a time window.</summary>
        private IEnumerator Measure(float seconds, float[] result)
        {
            float peak = 0;
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                for (int ch = 0; ch < 2; ch++)
                {
                    AudioListener.GetOutputData(buffer, ch);
                    for (int i = 0; i < buffer.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(buffer[i]));
                }
                yield return null;
            }
            result[0] = peak;
        }

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(1.0f);
            var audio = game.Audio;
            var s = game.Settings;
            var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            var cfg = AudioSettings.GetConfiguration();
            sb.AppendLine("AF-MilitaryShogi audio probe " + GameBootstrap.Version);
            sb.AppendLine("output: driver sample rate " + AudioSettings.outputSampleRate + " Hz, speaker mode " + cfg.speakerMode + ", DSP buffer " + cfg.dspBufferSize
                + ", AudioListener.volume " + AudioListener.volume + ", AudioListener.pause " + AudioListener.pause);
            sb.AppendLine("listeners: " + listeners.Length + string.Concat(listeners.Select(l => " [" + l.gameObject.name + " enabled=" + l.enabled + " active=" + l.gameObject.activeInHierarchy + "]")));
            var src = audio != null ? audio.Source : null;
            sb.AppendLine("source: " + (src == null ? "none" : "enabled=" + src.enabled + " active=" + src.gameObject.activeInHierarchy + " mute=" + src.mute + " volume=" + src.volume
                + " spatialBlend=" + src.spatialBlend + " mixerGroup=" + (src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "none (direct)")));
            sb.AppendLine("settings: sfx=" + s.SfxOn + " volume=" + s.SfxVolume + "% (from " + UserData.SettingsPath + ")");
            foreach (Sfx k in System.Enum.GetValues(typeof(Sfx)))
                foreach (var c in audio.Clips(k))
                    sb.AppendLine("clip " + c.name + ": " + c.length.ToString("0.000") + " s, " + c.channels + " ch, " + c.frequency + " Hz, loadType " + c.loadType + ", loadState " + c.loadState);
            sb.AppendLine();

            Check(listeners.Length == 1 && listeners[0].enabled && listeners[0].gameObject.activeInHierarchy, "exactly one enabled AudioListener");
            Check(src != null && src.enabled && !src.mute && src.spatialBlend == 0f, "AudioSource present, enabled, not muted, 2D");
            Check(audio.HasAllClips, "all 9 clips loaded from Resources/Audio");

            var r = new float[1];
            bool sfx0 = s.SfxOn; int vol0 = s.SfxVolume;
            s.SfxOn = true; s.SfxVolume = 100;
            yield return Measure(0.4f, r);
            float silence = r[0];
            sb.AppendLine("baseline peak (nothing playing): " + silence.ToString("0.0000"));

            // 2. Direct path, no game events.
            var select = audio.Clips(Sfx.Select)[0];
            src.volume = 1f;
            src.PlayOneShot(select);
            yield return Measure(0.4f, r);
            float direct = r[0];
            Check(direct > 0.02f, "direct PlayOneShot(sfx_select) reaches the output mix: peak " + direct.ToString("0.0000"));

            // 3. Game path: click an own piece in setup (GameController → AudioDirector.Play(Select)).
            yield return new WaitForSecondsRealtime(0.3f);
            int before = audio.Log.Count;
            int node = game.PlayerFormation.Pieces.First().Key;
            game.ClickNode(node);
            yield return Measure(0.4f, r);
            float viaGame = r[0];
            Check(audio.Log.Count > before && audio.Log.Last().Kind == Sfx.Select, "piece click requested Sfx.Select");
            Check(viaGame > 0.02f, "game path (piece click) reaches the output mix: peak " + viaGame.ToString("0.0000"));
            game.ClickNode(-1);

            // 4a. 効果音 OFF.
            yield return new WaitForSecondsRealtime(0.3f);
            s.SfxOn = false;
            yield return null;
            audio.Play(Sfx.Clash);
            yield return Measure(0.4f, r);
            Check(r[0] < 0.005f, "効果音 OFF is silent: peak " + r[0].ToString("0.0000") + ", source.volume " + src.volume);

            // 4b. Volume 100% vs 50% on the same clip.
            s.SfxOn = true; s.SfxVolume = 100;
            yield return null;
            audio.Play(Sfx.End);
            yield return Measure(0.8f, r);
            float full = r[0];
            yield return new WaitForSecondsRealtime(0.3f);
            s.SfxVolume = 50;
            yield return null;
            audio.Play(Sfx.End);
            yield return Measure(0.8f, r);
            float half = r[0];
            Check(full > 0.02f && Mathf.Abs(half / full - 0.5f) < 0.1f, "volume reaches the source: peak 100% " + full.ToString("0.0000") + ", 50% " + half.ToString("0.0000") + ", source.volume " + src.volume);

            s.SfxOn = sfx0; s.SfxVolume = vol0;
            sb.AppendLine(pass ? "PASS" : "FAIL");
            File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));
            Application.Quit(pass ? 0 : 1);
        }
    }
}
