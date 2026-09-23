using System;
using UnityEngine;

namespace MilitaryShogi.Game
{
    public enum PresentationMode { Play, Research }

    /// <summary>
    /// Presentation state only: which UI (対戦/研究) is shown, whether 「あそびかた」 is open and how
    /// the camera frames the board. Changing the mode never touches <see cref="GameSession"/>,
    /// the random state or the CPU – it only swaps UI and camera layout.
    /// </summary>
    public sealed class Presentation : MonoBehaviour
    {
        public PresentationMode Mode { get; private set; } = PresentationMode.Play;   // default at start-up: 対戦
        public bool HelpOpen { get; private set; }
        public event Action<PresentationMode> ModeChanged;

        /// <summary>Screen area (pixels, GUI coordinates) the research UI leaves for the board.</summary>
        public Rect ResearchBoardArea;
        /// <summary>Top bar / bottom bar height in play mode (pixels).</summary>
        public float PlayTop = 72f, PlayBottom = 36f;

        private GameController game;
        private Rect lastRect;
        private PresentationMode lastMode = (PresentationMode)(-1);

        public void Bind(GameController controller) { game = controller; }

        public void SetMode(PresentationMode mode)
        {
            if (mode == Mode) return;
            Mode = mode;
            if (ModeChanged != null) ModeChanged(mode);
        }

        public void Toggle() { SetMode(Mode == PresentationMode.Play ? PresentationMode.Research : PresentationMode.Play); }

        public void OpenHelp()
        {
            HelpOpen = true;
            game.SetPaused(true);
        }

        public void CloseHelp()
        {
            HelpOpen = false;
            game.SetPaused(false);
        }

        private void LateUpdate()
        {
            if (game == null || game.MainCamera == null) return;
            game.Graveyard.SetVisible(Mode == PresentationMode.Play);
            Rect rect;
            if (Mode == PresentationMode.Play)
                rect = new Rect(0, PlayBottom / Screen.height, 1, 1 - (PlayTop + PlayBottom) / Screen.height);
            else
                rect = new Rect(ResearchBoardArea.x / Screen.width, 0, ResearchBoardArea.width / Screen.width, 1f - ResearchBoardArea.y / Screen.height);
            if (rect.width <= 0 || rect.height <= 0) return;
            if (rect != lastRect || Mode != lastMode)
            {
                lastRect = rect;
                lastMode = Mode;
                game.MainCamera.rect = rect;
                CameraFit.Fit(game.MainCamera, Mode == PresentationMode.Play, Mode == PresentationMode.Play ? 0.012f : 0.02f);
            }
        }
    }
}
