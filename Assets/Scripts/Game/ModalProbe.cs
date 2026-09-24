using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Modal input check with real OS mouse clicks (-modalprobe REPORT_FILE). The cursor is moved and
    /// clicked through Windows (SetCursorPos + mouse_event), so input takes the same path as a person's:
    /// OS → Unity Input / IMGUI events → UI and board. Covers the 1.2.1 acceptance findings: clicks
    /// behind 設定 / あそびかた reaching background UI and the board, and the 「閉じる」 click that also
    /// pressed 「おまかせ配置」 underneath. Moves the real cursor for about 30 s; do not touch the mouse.
    /// Writes the report and quits (exit 0 = all checks passed).
    /// </summary>
    public sealed class ModalProbe : MonoBehaviour
    {
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        private const uint LeftDown = 0x0002, LeftUp = 0x0004;

        private GameController game;
        private Presentation presentation;
        private PlayUi playUi;
        private string reportPath;
        private IntPtr window;
        private readonly StringBuilder sb = new StringBuilder();
        private bool pass = true;

        public void Begin(GameController controller, string path)
        {
            game = controller;
            presentation = GetComponent<Presentation>();
            playUi = GetComponent<PlayUi>();
            reportPath = path;
            StartCoroutine(Run());
        }

        private string lastBefore;

        private void Check(bool ok, string what)
        {
            if (!ok) pass = false;
            sb.AppendLine((ok ? "OK   " : "FAIL ") + what);
            if (!ok && lastBefore != null)
            {
                var a = lastBefore.Split('|');
                var c = State().Split('|');
                string[] names = { "formation", "playerSeed", "strength", "temperament", "presetSlot", "presetSaving", "mode", "confirmNew", "selected", "phase", "fingerprint" };
                for (int i = 0; i < a.Length && i < c.Length; i++)
                    if (a[i] != c[i]) sb.AppendLine("       changed " + names[i] + ": " + (a[i].Length > 24 ? a[i].Substring(0, 24) + "…" : a[i]) + " → " + (c[i].Length > 24 ? c[i].Substring(0, 24) + "…" : c[i]));
                sb.AppendLine("       settingsOpen=" + presentation.SettingsOpen + " helpOpen=" + presentation.HelpOpen + " top=" + (ModalInput.Top ?? "none") + " blocked=" + ModalInput.PointerBlocked);
            }
        }

        private static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        private void MoveTo(Vector2 guiPixel)
        {
            var p = new POINT { X = Mathf.RoundToInt(guiPixel.x), Y = Mathf.RoundToInt(guiPixel.y) };
            ClientToScreen(window, ref p);
            SetCursorPos(p.X, p.Y);
        }

        /// <summary>A real left click at a window pixel (top-left origin). Frames between the steps, like a person.</summary>
        private IEnumerator Click(Vector2 at)
        {
            MoveTo(at);
            yield return Frames(4);
            mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
            yield return Frames(4);
            mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
            yield return Frames(6);
        }

        private IEnumerator EnsureSettingsOpen()
        {
            if (presentation.SettingsOpen) yield break;
            yield return Click(Spot("play.settings"));
            if (!presentation.SettingsOpen) Check(false, "could not reopen 設定");
        }

        private Vector2 Spot(string key)
        {
            Vector2 v;
            if (!UiKit.Spots.TryGetValue(key, out v)) { Check(false, "control position not recorded: " + key); return new Vector2(-1, -1); }
            return v;
        }

        private Vector2 BoardPoint(int node)
        {
            var sp = game.MainCamera.WorldToScreenPoint(BoardLayout.Node(node) + Vector3.up * 0.1f);
            return new Vector2(sp.x, Screen.height - sp.y);
        }

        /// <summary>Everything a background click could change.</summary>
        private string State()
        {
            var s = game.Settings;
            return string.Join("|", game.PlayerFormation.Signature(), s.PlayerFormationSeed, s.Strength, s.Temperament, playUi.Presets.SelectedSlot, playUi.Presets.Saving,
                presentation.Mode, playUi.ConfirmNewOpen, game.SelectedNode, game.Phase, game.Session.Started ? game.Session.Fingerprint() : "-");
        }

        private IEnumerator Run()
        {
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(1.5f);
            window = GetActiveWindow();
            if (window == IntPtr.Zero) window = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            SetForegroundWindow(window);
            yield return new WaitForSecondsRealtime(0.5f);
            sb.AppendLine("AF-MilitaryShogi modal probe " + GameBootstrap.Version + " (" + Screen.width + "x" + Screen.height + ", real OS clicks)");
            Vector2 blank = new Vector2(40, Screen.height - 140);
            int ownPiece = game.PlayerFormation.Pieces.First(p => p.Value != PieceType.Mine && p.Value != PieceType.Flag).Key;

            sb.AppendLine("     start: focused=" + Application.isFocused + " top=" + (ModalInput.Top ?? "none") + " pointerBlocked=" + ModalInput.PointerBlocked
                + " holding=" + ModalInput.HoldingAfterClose + " buttonHeld=" + Input.GetMouseButton(0) + " spots=" + UiKit.Spots.Count);
            // Sanity: without a modal the same clicks work (so a "no change" below really means blocked).
            yield return Click(BoardPoint(ownPiece));
            Check(game.SelectedNode == ownPiece, "no modal: a board click selects the piece (hover " + game.HoverNode + ", focused " + Application.isFocused + ", blocked " + ModalInput.PointerBlocked + ")");
            yield return Click(BoardPoint(ownPiece));
            yield return Click(Spot("play.settings"));
            Check(presentation.SettingsOpen, "no modal: the real 「設定」 click opens 設定");

            // ---- 設定 (setup) ----
            // 設定 covers part of the setup panel; a click "on" a covered control lands on the modal itself
            // (possibly on one of its buttons, e.g. 閉じる over 「おまかせ配置」). Either way the control
            // behind must not fire. If a click closed 設定, it is reopened for the next check.
            foreach (var target in new[] { "setup.omakase", "setup.strongest", "preset.slot2", "preset.save", "board", "blank" })
            {
                yield return EnsureSettingsOpen();
                string b0 = State(); lastBefore = b0;
                var at = target == "board" ? BoardPoint(ownPiece) : target == "blank" ? blank : Spot(target);
                yield return Click(at);
                bool closed = !presentation.SettingsOpen;
                Check(State() == b0 && (target != "board" || game.HoverNode == -1),
                    "設定: click at " + target + " " + at + (closed ? " (landed on 設定's 「閉じる」, closed it)" : " (設定 stays open)") + " → nothing behind changed");
                if (target == "blank") Check(!closed, "設定: click on empty space does not close 設定");
            }
            yield return EnsureSettingsOpen();
            string before = State(); lastBefore = before;

            // The close button: report what is underneath, then click it.
            var close = Spot("settings.close");
            var omakase = Spot("setup.omakase");
            sb.AppendLine("     settings 「閉じる」 at " + close + ", 「おまかせ配置」 at " + omakase + " (distance " + Vector2.Distance(close, omakase).ToString("0") + " px)");
            yield return Click(close);
            Check(!presentation.SettingsOpen, "設定: 「閉じる」 closes 設定");
            Check(State() == before, "設定: the 「閉じる」 click does not reach the UI behind (placement unchanged)");
            yield return Frames(3);
            Check(!ModalInput.PointerBlocked, "after closing, pointer input is back (released)");
            yield return Click(omakase);
            Check(State() != before, "the next click on 「おまかせ配置」 works normally");
            yield return Click(Spot("play.help"));
            Check(presentation.HelpOpen, "real 「あそびかた」 click opens あそびかた (setup)");
            before = State(); lastBefore = before;
            yield return Click(Spot("help.close"));
            Check(!presentation.HelpOpen && !presentation.SettingsOpen && State() == before, "あそびかた 「閉じる」 closes only it (setup, nothing behind fired)");

            // ---- In game ----
            game.StartGame();
            yield return Frames(10);
            yield return Click(Spot("play.settings"));
            Check(presentation.SettingsOpen, "in game: 「設定」 opens");
            double c0 = game.ElapsedSeconds;
            yield return new WaitForSecondsRealtime(1.5f);
            double c1 = game.ElapsedSeconds;
            Check(c1 - c0 > 1.2, "設定 open: clock keeps running (+" + (c1 - c0).ToString("0.00") + " s)");
            before = State(); lastBefore = before;
            yield return Click(BoardPoint(ownPiece));
            Check(State() == before, "設定 (in game): board click → game state unchanged");
            yield return Click(Spot("settings.close"));
            Check(!presentation.SettingsOpen && State() == before, "設定 (in game): 「閉じる」 only closes 設定");

            // ---- あそびかた ----
            yield return Click(Spot("play.help"));
            Check(presentation.HelpOpen, "in game: 「あそびかた」 opens");
            before = State(); lastBefore = before;
            yield return Click(Spot("play.research"));
            Check(presentation.Mode == PresentationMode.Play && State() == before, "あそびかた: click on 「研究モードへ」 behind → no mode change");
            // Press and hold on 「設定」 behind: no pressed state, no dialog.
            MoveTo(Spot("play.settings"));
            yield return Frames(4);
            mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
            yield return Frames(6);
            int hot = playUi.HotControlAtRepaint;
            mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
            yield return Frames(6);
            Check(hot == 0, "あそびかた: holding the mouse on 「設定」 behind → no pressed control (hotControl " + hot + ")");
            Check(!presentation.SettingsOpen && presentation.HelpOpen, "あそびかた: 「設定」 behind does not open 設定");
            yield return Click(BoardPoint(ownPiece));
            Check(State() == before, "あそびかた: board click → game state unchanged");
            yield return Click(blank);
            Check(presentation.HelpOpen && State() == before, "あそびかた: click on empty space → stays open, nothing changes");
            c0 = game.ElapsedSeconds;
            yield return new WaitForSecondsRealtime(1.2f);
            c1 = game.ElapsedSeconds;
            Check(Math.Abs(c1 - c0) < 1e-6 && game.Paused, "あそびかた open: game paused and clock stopped (+" + (c1 - c0).ToString("0.000") + " s)");
            yield return Click(Spot("help.close"));
            Check(!presentation.HelpOpen && State() == before, "あそびかた: 「閉じる」 only closes it (state unchanged, mode " + presentation.Mode + ")");
            c0 = game.ElapsedSeconds;
            yield return new WaitForSecondsRealtime(1.2f);
            c1 = game.ElapsedSeconds;
            Check(c1 - c0 > 0.9 && !game.Paused, "after あそびかた: clock runs again (+" + (c1 - c0).ToString("0.00") + " s)");
            yield return Click(BoardPoint(ownPiece));
            Check(game.SelectedNode == ownPiece, "after closing: the next board click selects normally (selected " + game.SelectedNode + ", hover " + game.HoverNode
                + ", phase " + game.Phase + ", blocked " + ModalInput.PointerBlocked + ", overUi " + game.IsOverUi(BoardPoint(ownPiece)) + ")");
            // ---- 1.4.0: tap-safe selection with real clicks ----
            {
                var view = game.View;
                var sel = view.Own.First(p => p.Alive && MoveRules.Generate(p.Type, GameController.Human, p.Node, view.Owners).Count > 0);
                yield return Click(BoardPoint(sel.Node));
                var tg = game.PlayTargets();
                int miss = Enumerable.Range(0, BoardGraph.CellCount).First(n => view.Owners[n] == MoveRules.Empty && !tg.Contains(n));
                string st = State();
                yield return Click(BoardPoint(miss));
                Check(game.SelectedNode == sel.Node && State() == st, "real click on a non-target square keeps the selection (no move)");
                yield return Click(BoardPoint(sel.Node));
                Check(game.SelectedNode == sel.Node, "real click on the selected piece keeps it");
                yield return Click(blank);
                Check(game.SelectedNode == -1, "real click off the board deselects");
            }

            // ---- 1.3.0: referee notice and result are modals too ----
            string beforeNotice = State();
            game.TestJudgeNotice();
            yield return Frames(6);
            before = State(); lastBefore = before;
            // "Behind" means outside the notice: a board piece left or right of the dialog.
            Vector2 tl = Spot("judge.panelTL"), br = Spot("judge.panelBR");
            int outside = game.View.Own.Where(p => p.Alive).Select(p => p.Node)
                .OrderBy(n => Mathf.Min(Mathf.Abs(BoardPoint(n).x - tl.x), Mathf.Abs(BoardPoint(n).x - br.x)))
                .FirstOrDefault(n => BoardPoint(n).x < tl.x - 10 || BoardPoint(n).x > br.x + 10 || BoardPoint(n).y > br.y + 10 || BoardPoint(n).y < tl.y - 10);
            yield return Click(Spot("play.research"));
            yield return Click(BoardPoint(outside));
            yield return Click(blank);
            Check(game.ResignNoticeOpen && State() == before && presentation.Mode == PresentationMode.Play, "審判の確認: clicks behind (研究モードへ, board, empty space) do nothing");
            c0 = game.ElapsedSeconds;
            yield return new WaitForSecondsRealtime(1.0f);
            Check(Math.Abs(game.ElapsedSeconds - c0) < 1e-6, "審判の確認: clock stopped");
            yield return Click(Spot("judge.continue"));
            lastBefore = beforeNotice;   // 続行 returns to exactly the state before the notice (player's turn)
            Check(!game.ResignNoticeOpen && State() == beforeNotice, "審判の確認: 「続行」 closes it and does not reach the UI behind");
            // Result: a new game, resign through the real 「投了」 button.
            game.NewSetup(false);
            game.StartGame();
            yield return Frames(10);
            game.TestJudgeNotice();
            yield return Frames(6);
            yield return Click(Spot("judge.resign"));
            yield return Frames(10);
            Check(game.IsFinished && presentation.ResultOpen && ModalInput.Top == "result", "「投了」 ends the game and shows the result modal");
            before = State(); lastBefore = before;
            yield return Click(Spot("play.research"));
            yield return Click(Spot("play.settings"));
            Check(presentation.Mode == PresentationMode.Play && !presentation.SettingsOpen && State() == before, "結果: header buttons behind do nothing");
            yield return Click(Spot("result.board"));
            Check(!presentation.ResultOpen && presentation.Mode == PresentationMode.Play && !presentation.SettingsOpen, "結果: 「盤面を見る」 closes only the result");
            yield return Click(Spot("play.settings"));
            Check(presentation.SettingsOpen, "after the result: header works again");
            yield return Click(Spot("settings.close"));

            Check(UiGuard.ErrorCount == 0, "no UI exceptions (" + UiGuard.ErrorCount + ")");

            sb.AppendLine(pass ? "PASS" : "FAIL");
            File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));
            Application.Quit(pass ? 0 : 1);
        }
    }
}
