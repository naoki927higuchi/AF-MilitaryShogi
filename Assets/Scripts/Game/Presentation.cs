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

        public bool GraveyardsVisible { get { return (Mode == PresentationMode.Play || !MonitorOpen) && !HideGraveyards3D; } }

        /// <summary>Result dialog (modal). 「盤面を見る」 dismisses it until the next game.</summary>
        public bool ResultDismissed;
        public bool ResultOpen { get { return game != null && game.Phase == Phase.Finished && !ResultDismissed; } }

        /// <summary>Android portrait shows the losses as compact 2D rows instead of the 3D tables.</summary>
        public bool HideGraveyards3D;
        /// <summary>Android: the board area (GUI pixels) chosen by the mobile layout; replaces PlayTop/PlayBottom.</summary>
        public Rect? PlayAreaOverride;
        /// <summary>Research mode is not available (Android).</summary>
        public bool ResearchAvailable = true;
        public bool EnemyRevealActive { get { return Mode == PresentationMode.Research && RevealCpuPieces; } }

        private GameController game;
        private Rect lastRect;
        private bool lastGraves;
        private PresentationMode lastMode = (PresentationMode)(-1);
        public int WatchdogRepairs { get; private set; }

        public void Bind(GameController controller)
        {
            game = controller;
            // Input-modal UI (see ModalInput). The referee's notice and help are in front of everything.
            ModalInput.Register("judge", 110, () => game.ResignNoticeOpen);
            ModalInput.Register("help", 100, () => HelpOpen);
            ModalInput.Register("settings", 50, () => SettingsOpen);
            ModalInput.Register("result", 40, () => ResultOpen);
            controller.SetupStarted += () => ResultDismissed = false;
        }

        public void SetMode(PresentationMode mode)
        {
            if (mode == Mode || (mode == PresentationMode.Research && !ResearchAvailable)) return;
            Mode = mode;
            SettingsOpen = false;
            if (ModeChanged != null) ModeChanged(mode);
        }

        public void Toggle() { if (ResearchAvailable) SetMode(Mode == PresentationMode.Play ? PresentationMode.Research : PresentationMode.Play); }

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

        // ------------------------------------------------------------------
        // Android lifecycle: in the background (home, other app, screen off, focus lost) the game is
        // paused – no CPU turn, no animation, no clock – and resumes in the same session.
        // ------------------------------------------------------------------

        private bool appPaused, appUnfocused;
        /// <summary>How often the app went to the background (for tests and diagnostics).</summary>
        public int BackgroundCount { get; private set; }
        public bool InBackground { get { return appPaused || appUnfocused; } }

        private void OnApplicationPause(bool paused) { appPaused = paused; ApplyBackground(); }
        private void OnApplicationFocus(bool focused) { appUnfocused = !focused; ApplyBackground(); }

        /// <summary>Simulate the lifecycle (probe on PC/device).</summary>
        public void SimulateBackground(bool background) { appPaused = background; appUnfocused = false; ApplyBackground(); }

        private void ApplyBackground()
        {
            if (game == null || !UiKit.Mobile) return;   // PC: alt-tab does not pause a running game
            bool pausedBefore = (game.PauseReasons & GameController.PauseReason.Background) != 0;
            if (InBackground && !pausedBefore) BackgroundCount++;
            game.SetPause(GameController.PauseReason.Background, InBackground);
        }

        private void Update()
        {
            if (game == null) return;
            // One key toggles 対戦⇔研究 (the header buttons do the same). Not while a text field has focus.
            if (!ModalInput.AnyOpen && GUIUtility.keyboardControl == 0 && Input.GetKeyDown(ToggleKey)) Toggle();
        }

        private void LateUpdate()
        {
            if (game == null || game.MainCamera == null) return;
            Watchdog();
            game.Graveyard.SetVisible(GraveyardsVisible);
            game.SetEnemyReveal(EnemyRevealActive);
            Rect rect;
            if (Mode == PresentationMode.Play && PlayAreaOverride.HasValue)
            {
                var a = PlayAreaOverride.Value;   // GUI pixels (top-left origin) → viewport
                rect = new Rect(a.x / Screen.width, 1f - a.yMax / Screen.height, a.width / Screen.width, a.height / Screen.height);
            }
            else if (Mode == PresentationMode.Play)
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
