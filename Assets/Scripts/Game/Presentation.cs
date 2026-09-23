using System;
using UnityEngine;

namespace MilitaryShogi.Game
{
    public enum PresentationMode { Play, Research }

    /// <summary>
    /// Presentation state only: which UI (対戦/研究) is shown, the research monitor, the research-only
    /// reveal switch, open overlays (あそびかた・設定) and how the camera frames the board. Changing any
    /// of this never touches <see cref="GameSession"/>, the random state or the CPU.
    ///
    /// Loss areas: shown in 対戦 and in 研究 while the thinking monitor is closed; hidden while the
    /// monitor uses the right half of the screen. Enemy faces on the board: only in 研究 with
    /// 「CPU駒の正体を表示」 on; never in 対戦 and never in the loss areas.
    /// </summary>
    public sealed class Presentation : MonoBehaviour
    {
        public PresentationMode Mode { get; private set; } = PresentationMode.Play;   // default at start-up: 対戦
        public bool HelpOpen { get; private set; }
        public bool SettingsOpen;
        public bool MonitorOpen;          // research thinking monitor
        public bool RevealCpuPieces;      // research 「CPU駒の正体を表示」
        public event Action<PresentationMode> ModeChanged;

        public const KeyCode ToggleKey = KeyCode.F2;

        /// <summary>Screen area (pixels, GUI coordinates) the research UI leaves for the board.</summary>
        public Rect ResearchBoardArea;
        /// <summary>Top bar / bottom bar height in play mode (pixels).</summary>
        public float PlayTop = 72f, PlayBottom = 36f;

        public bool GraveyardsVisible { get { return Mode == PresentationMode.Play || !MonitorOpen; } }
        public bool EnemyRevealActive { get { return Mode == PresentationMode.Research && RevealCpuPieces; } }

        private GameController game;
        private Rect lastRect;
        private bool lastGraves;
        private PresentationMode lastMode = (PresentationMode)(-1);
        public int WatchdogRepairs { get; private set; }

        public void Bind(GameController controller) { game = controller; }

        public void SetMode(PresentationMode mode)
        {
            if (mode == Mode) return;
            Mode = mode;
            SettingsOpen = false;
            if (ModeChanged != null) ModeChanged(mode);
        }

        public void Toggle() { SetMode(Mode == PresentationMode.Play ? PresentationMode.Research : PresentationMode.Play); }

        public void OpenHelp()
        {
            HelpOpen = true;
            SettingsOpen = false;
            game.SetPause(GameController.PauseReason.Help, true);
        }

        public void CloseHelp()
        {
            HelpOpen = false;
            game.SetPause(GameController.PauseReason.Help, false);
        }

        private void Update()
        {
            if (game == null) return;
            // One key toggles 対戦⇔研究 (the header buttons do the same). Not while a text field has focus.
            if (!HelpOpen && GUIUtility.keyboardControl == 0 && Input.GetKeyDown(ToggleKey)) Toggle();
        }

        private void LateUpdate()
        {
            if (game == null || game.MainCamera == null) return;
            Watchdog();
            game.Graveyard.SetVisible(GraveyardsVisible);
            game.SetEnemyReveal(EnemyRevealActive);
            Rect rect;
            if (Mode == PresentationMode.Play)
                rect = new Rect(0, PlayBottom / Screen.height, 1, 1 - (PlayTop + PlayBottom) / Screen.height);
            else
                rect = new Rect(ResearchBoardArea.x / Screen.width, 0, ResearchBoardArea.width / Screen.width, 1f - ResearchBoardArea.y / Screen.height);
            if (rect.width <= 0 || rect.height <= 0) return;
            bool graves = GraveyardsVisible;
            if (rect != lastRect || Mode != lastMode || graves != lastGraves)
            {
                lastRect = rect;
                lastMode = Mode;
                lastGraves = graves;
                game.MainCamera.rect = rect;
                CameraFit.Fit(game.MainCamera, graves, 0.02f);
            }
        }

        /// <summary>
        /// Keeps overlay state and input state consistent: the pause (and time scale) follows the
        /// help overlay exactly, so no hidden overlay can keep the game or its input blocked.
        /// </summary>
        private void Watchdog()
        {
            bool helpPause = (game.PauseReasons & GameController.PauseReason.Help) != 0;
            if (helpPause != HelpOpen) { game.SetPause(GameController.PauseReason.Help, HelpOpen); WatchdogRepairs++; }
            float expected = game.Paused ? 0f : 1f;
            if (Time.timeScale != expected) { Time.timeScale = expected; WatchdogRepairs++; }
            if (HelpOpen && SettingsOpen) { SettingsOpen = false; WatchdogRepairs++; }
        }
    }
}
