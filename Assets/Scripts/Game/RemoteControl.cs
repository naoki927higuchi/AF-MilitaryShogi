using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using MilitaryShogi.Cpu;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Test harness for the Android device (-remote, passed as the launch intent's "args" extra). The
    /// host drives the real app with real touches (adb shell input tap), rotation and lifecycle events,
    /// and uses this file channel only to read the app's state and to trigger test-only actions:
    ///   host writes  &lt;data&gt;/remote/cmd.txt  = "&lt;seq&gt; &lt;command&gt; [args]"
    ///   app writes   &lt;data&gt;/remote/state.txt = "seq=&lt;seq&gt;" + key=value lines (screen pixels, top-left origin)
    /// Commands: state · automove (one player move through the click handler, like the PC auto-test)
    /// · judge (open the referee notice) · orient portrait|landscape|auto · bg 1|0 (simulated lifecycle)
    /// · audio (play sfx_select and measure the real output mix) · seeds P,C,D (new setup with seeds)
    /// · shot NAME (screenshot into the remote folder) · open settings|help (screenshots only; tests tap).
    /// Not started in a normal launch.
    /// </summary>
    public sealed class RemoteControl : MonoBehaviour
    {
        private GameController game;
        private Presentation presentation;
        private MobileUi mobile;
        private string dir;
        private int lastSeq = -1;
        private CpuPlayer driver;
        private float lastPeak;
        private float nextPoll;

        public void Begin(GameController controller)
        {
            game = controller;
            presentation = GetComponent<Presentation>();
            mobile = GetComponent<MobileUi>();
            dir = Path.Combine(UserData.Directory, "remote");
            Directory.CreateDirectory(dir);
            // The app creates the command file itself (Android 17 does not let the app read a file the
            // adb shell created); the host only overwrites its contents.
            try { File.WriteAllText(Path.Combine(dir, "cmd.txt"), ""); } catch (Exception) { }
            WriteState(0);
        }

        private void Update()
        {
            if (game == null || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.1f;
            string path = Path.Combine(dir, "cmd.txt");
            if (!File.Exists(path)) return;
            string text;
            try { text = File.ReadAllText(path).Trim(); } catch (Exception) { return; }
            var parts = text.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            int seq;
            if (parts.Length < 2 || !int.TryParse(parts[0], out seq) || seq == lastSeq) return;
            lastSeq = seq;
            StartCoroutine(Execute(seq, parts[1], parts.Length > 2 ? parts[2] : ""));
        }

        private IEnumerator Execute(int seq, string command, string arg)
        {
            switch (command)
            {
                case "automove":
                    if (game.Phase == Phase.PlayerTurn)
                    {
                        if (driver == null) driver = new CpuPlayer(GameController.Human, game.Settings.PlayerFormationSeed, 777);
                        var cmd = driver.Decide(game.View).Chosen.Command;
                        game.ClickNode(game.View.OwnById(cmd.PieceId).Node);
                        game.ClickNode(cmd.To);
                    }
                    break;
                case "judge": game.TestJudgeNotice(); break;
                case "stalemate": game.TestJudgeNotice(JudgeNoticeKind.Stalemate); break;
                case "start": game.StartGame(); break;
                case "orient":
                    Screen.orientation = arg == "portrait" ? ScreenOrientation.Portrait : arg == "landscape" ? ScreenOrientation.LandscapeLeft : ScreenOrientation.AutoRotation;
                    if (!Application.isMobilePlatform)
                    {
                        // PC emulation of a phone: swap the window's width and height.
                        int a = Mathf.Max(Screen.width, Screen.height), b = Mathf.Min(Screen.width, Screen.height);
                        if (arg == "portrait") Screen.SetResolution(b, a, FullScreenMode.Windowed);
                        else if (arg == "landscape") Screen.SetResolution(a, b, FullScreenMode.Windowed);
                    }
                    break;
                case "bg": presentation.SimulateBackground(arg == "1"); break;
                case "seeds":
                    var s = arg.Split(',');
                    game.Settings.PlayerFormationSeed = int.Parse(s[0]);
                    game.Settings.CpuFormationSeed = int.Parse(s[1]);
                    game.Settings.CpuDecisionSeed = int.Parse(s[2]);
                    driver = null;
                    game.NewSetup(false);
                    break;
                case "shot":
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, arg));
                    yield return null;
                    break;
                case "open":
                    if (arg == "settings") presentation.SettingsOpen = true;
                    else if (arg == "help") presentation.OpenHelp();
                    break;
                case "audio":
                    yield return MeasureAudio();
                    break;
            }
            yield return null;
            yield return null;
            WriteState(seq);
        }

        private IEnumerator MeasureAudio()
        {
            var buffer = new float[1024];
            game.Audio.Play(Sfx.Select);
            float peak = 0, end = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < end)
            {
                for (int ch = 0; ch < 2; ch++)
                {
                    AudioListener.GetOutputData(buffer, ch);
                    foreach (var v in buffer) peak = Mathf.Max(peak, Mathf.Abs(v));
                }
                yield return null;
            }
            lastPeak = peak;
        }

        private static string P(Vector2 v) { return Mathf.RoundToInt(v.x) + "," + Mathf.RoundToInt(v.y); }

        private string NodeScreen(int node)
        {
            var sp = game.MainCamera.WorldToScreenPoint(BoardLayout.Node(node) + Vector3.up * 0.1f);
            return P(new Vector2(sp.x, Screen.height - sp.y));
        }

        private void WriteState(int seq)
        {
            var sb = new StringBuilder();
            Action<string, object> kv = (k, v) => sb.Append(k).Append('=').Append(v).Append('\n');
            kv("seq", seq);
            kv("version", GameBootstrap.Version);
            kv("mobile", UiKit.Mobile);
            kv("screen", Screen.width + "x" + Screen.height);
            var sa = Screen.safeArea;
            kv("safe", Mathf.RoundToInt(sa.x) + "," + Mathf.RoundToInt(Screen.height - sa.yMax) + "," + Mathf.RoundToInt(sa.width) + "," + Mathf.RoundToInt(sa.height));
            kv("orientation", Screen.orientation);
            kv("portrait", mobile != null ? mobile.Portrait : Screen.height >= Screen.width);
            kv("scale", UiKit.Scale.ToString("0.000"));
            kv("dpi", Screen.dpi);
            kv("phase", game.Phase);
            kv("ply", game.Ply);
            kv("toMove", game.ToMove);
            kv("selected", game.SelectedNode);
            kv("inspected", game.InspectedPieceId);
            kv("tooltip", game.Tooltip.LastText == null ? "" : game.Tooltip.LastText.Split('\n')[0].Replace("<b>", "").Replace("</b>", ""));
            kv("modalTop", ModalInput.Top ?? "none");
            kv("pointerBlocked", ModalInput.PointerBlocked);
            kv("settingsOpen", presentation.SettingsOpen);
            kv("helpOpen", presentation.HelpOpen);
            kv("resultOpen", presentation.ResultOpen);
            kv("judgeOpen", game.ResignNoticeOpen);
            kv("noticeKind", game.NoticeKind);
            kv("confirmNew", mobile != null && mobile.ConfirmNewOpen);
            kv("sheetCollapsed", mobile != null && mobile.SheetCollapsed);
            kv("paused", game.PauseReasons);
            kv("background", presentation.InBackground);
            kv("backgroundCount", presentation.BackgroundCount);
            kv("timeScale", Time.timeScale);
            kv("clock", game.ElapsedSeconds.ToString("0.000"));
            kv("result", game.ResultText());
            kv("resultReason", game.Session.Match != null ? game.Session.Match.EndReason.ToString() : "-");
            kv("fingerprint", game.Session.Started ? game.Session.Fingerprint() : "-");
            kv("session", RuntimeHelpers.GetHashCode(game.Session));
            kv("cpu", RuntimeHelpers.GetHashCode(game.Cpu));
            kv("random", UnityEngine.Random.state.GetHashCode());
            kv("formation", game.PlayerFormation.Signature());
            kv("seeds", game.Settings.PlayerFormationSeed + "," + game.Settings.CpuFormationSeed + "," + game.Settings.CpuDecisionSeed);
            kv("strength", game.Settings.Strength);
            kv("temperament", game.Settings.Temperament);
            kv("sfx", game.Settings.SfxOn + "," + game.Settings.SfxVolume);
            kv("presets", string.Join("|", Enumerable.Range(0, FormationPresets.SlotCount).Select(i => game.Presets.Slot(i).Name + (game.Presets.Slot(i).IsEmpty ? "(empty)" : ""))));
            kv("presetSlot", mobile != null ? mobile.Presets.SelectedSlot : -1);
            kv("presetSaving", mobile != null && mobile.Presets.Saving);
            kv("listeners", FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(l => l.isActiveAndEnabled));
            kv("clips", game.Audio != null && game.Audio.HasAllClips);
            kv("audioLog", game.Audio != null ? game.Audio.Log.Count : 0);
            kv("audioPeak", lastPeak.ToString("0.0000"));
            kv("researchUi", GetComponent<ResearchUi>() != null);
            kv("graves3d", game.Graveyard.gameObject.activeSelf);
            kv("lossOwn", game.Graveyard.OwnViews.Count);
            kv("lossEnemy", game.Graveyard.EnemyViews.Count);
            kv("uiErrors", UiGuard.ErrorCount);
            kv("font", GameAssets.UiFont != null ? string.Join("/", GameAssets.UiFont.fontNames ?? new string[0]).Replace('\n', ' ') : "none");
            var cam = game.MainCamera.pixelRect;
            kv("camera", Mathf.RoundToInt(cam.x) + "," + Mathf.RoundToInt(Screen.height - cam.yMax) + "," + Mathf.RoundToInt(cam.width) + "," + Mathf.RoundToInt(cam.height));
            foreach (var spot in UiKit.Spots) kv("spot." + spot.Key, P(spot.Value));
            kv("squares", string.Join(";", Enumerable.Range(0, BoardGraph.NodeCount).Select(n => n + "@" + NodeScreen(n))));
            if (game.View != null && game.Session.Started)
            {
                kv("own", string.Join(";", game.View.Own.Where(p => p.Alive).Select(p => p.Node + ":" + p.Type + "@" + NodeScreen(p.Node))));
                kv("enemy", string.Join(";", game.View.Enemy.Where(p => p.Alive).Select(p => p.Id + ":" + p.Node + "@" + NodeScreen(p.Node))));
                if (game.SelectedNode >= 0)
                    kv("targets", string.Join(";", game.PlayTargets().Select(t => t + "@" + NodeScreen(t))));
            }
            else
                kv("own", string.Join(";", game.PlayerFormation.Pieces.Select(p => p.Key + ":" + p.Value + "@" + NodeScreen(p.Key))));
            try
            {
                string tmp = Path.Combine(dir, "state.tmp");
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                string final = Path.Combine(dir, "state.txt");
                if (File.Exists(final)) File.Delete(final);
                File.Move(tmp, final);
            }
            catch (Exception e) { Debug.LogWarning("remote state: " + e.Message); }
        }
    }
}
