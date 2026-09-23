using System.Linq;
using MilitaryShogi.Cpu;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// 対戦モード: the screen for simply playing. Top: title logo, TURN, whose turn, elapsed time,
    /// 研究モードへ / あそびかた / 設定 / 新規対局. Left and right of the board: 自軍の損失 / 敵軍の損失
    /// (3D pieces on the table, see <see cref="GraveyardView"/>). Bottom: one status line. No seeds,
    /// candidate moves, evaluations or probabilities. Enemy hover shows public observations only
    /// (and can be switched off in 設定).
    /// </summary>
    public sealed class PlayUi : MonoBehaviour
    {
        private GameController game;
        private Presentation presentation;
        private UiKit ui;
        private float scale, vw, vh;
        private bool confirmNew;
        private Texture2D logo;
        private SettingsPanel settings;
        private PresetPanel presets;

        /// <summary>Where the logo was drawn last frame (screen pixels, GUI coordinates) – for the auto-test.</summary>
        public Rect LogoScreenRect { get; private set; }
        public Rect[] HeadingScreenRects { get; private set; } = new Rect[2];
        public Rect ResearchButtonRect { get; private set; }
        public int ResultDrawCount { get; private set; }
        public int LastDrawFrame { get; private set; } = -1;
        /// <summary>IMGUI control being pressed at the last repaint (0 = none), for -modalprobe.</summary>
        public int HotControlAtRepaint { get; private set; }
        public string LastResultText { get; private set; }
        public Texture2D Logo { get { return logo; } }
        public PresetPanel Presets { get { return presets; } }
        public bool ConfirmNewOpen { get { return confirmNew; } set { confirmNew = value; } }

        public void Bind(GameController controller, Presentation presentation)
        {
            game = controller;
            this.presentation = presentation;
            logo = GameAssets.Texture("UI/title_logo");
            settings = new SettingsPanel(controller.Settings);
            presets = new PresetPanel(controller);
            controller.SetupStarted += () => { presets.Saving = false; presets.Message = ""; confirmNew = false; };
            ModalInput.Register("confirmNew", 50, () => presentation.Mode == PresentationMode.Play && confirmNew);
        }

        private void Update()
        {
            if (game == null || presentation.Mode != PresentationMode.Play || presentation.HelpOpen) return;
            // Keys follow the modal rule too: with a modal open only its own key (Esc = close it) works.
            string top = ModalInput.Top;
            if (top == null && Input.GetKeyDown(KeyCode.F1)) presentation.OpenHelp();
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (top == "settings") presentation.SettingsOpen = false;
                else if (top == "confirmNew") confirmNew = false;
                else if (top == null) presets.Saving = false;
            }
        }

        private void OnGUI()
        {
            if (game == null || presentation.Mode != PresentationMode.Play) return;
            UiGuard.Run("PlayUi", DrawGui, () => { confirmNew = false; presentation.SettingsOpen = false; presets.Saving = false; });
        }

        private void DrawGui()
        {
            if (ui == null) ui = new UiKit();
            scale = UiKit.Scale;
            vw = Screen.width / scale;
            vh = Screen.height / scale;
            presentation.PlayTop = 72f * scale;
            presentation.PlayBottom = 34f * scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            game.UiRects.Clear();
            if (Event.current.type == EventType.Repaint) LastDrawFrame = Time.frameCount;

            // Only the frontmost modal gets the pointer; everything behind it is drawn shielded.
            string top = ModalInput.Top;
            using (ModalInput.Background())
            {
                DrawLossHeadings();
                DrawTopBar();
                DrawStatusBar();
                if (game.Phase == Phase.Setup) DrawSetup();
                if (game.Phase == Phase.Finished) DrawResult();
            }
            if (presentation.SettingsOpen)
                using (ModalInput.Background(top != "settings")) DrawSettings();
            if (confirmNew)
                using (ModalInput.Background(top != "confirmNew")) DrawConfirmNew();
            game.Tooltip.Draw(presentation.HelpOpen || presentation.SettingsOpen || confirmNew, false, game.Settings.ObservationTooltip);
            if (Event.current.type == EventType.Repaint) HotControlAtRepaint = GUIUtility.hotControl;
        }

        private Rect Region(Rect r)
        {
            game.UiRects.Add(new Rect(r.x * scale, r.y * scale, r.width * scale, r.height * scale));
            return r;
        }

        private void DrawTopBar()
        {
            var bar = Region(new Rect(0, 0, vw, 72));
            ui.Fill(bar, new Color(0.04f, 0.03f, 0.02f, 0.88f));
            ui.Fill(new Rect(0, 71, vw, 1), new Color(0.75f, 0.55f, 0.28f, 0.6f));
            if (logo != null)
            {
                float h = 64f, w = h * logo.width / logo.height;
                var r = new Rect(14, 4, w, h);
                GUI.DrawTexture(r, logo, ScaleMode.ScaleToFit, true);
                LogoScreenRect = new Rect(r.x * scale, r.y * scale, r.width * scale, r.height * scale);
            }
            float x = Mathf.Max(250, LogoScreenRect.xMax / scale + 24);
            if (game.Phase == Phase.Setup)
                GUI.Label(new Rect(x, 20, 300, 32), "初期配置", ui.Big);
            else
            {
                GUI.Label(new Rect(x, 20, 160, 32), "TURN " + (game.Ply + (game.IsFinished ? 0 : 1)), ui.Big);
                bool mine = game.ToMove == GameController.Human;
                string who = game.IsFinished ? "終局" : mine ? "あなたの番" : "CPUの番";
                ui.Fill(new Rect(x + 170, 31, 12, 12), game.IsFinished ? Color.gray : mine ? new Color(0.45f, 0.9f, 0.5f) : new Color(0.95f, 0.55f, 0.35f));
                GUI.Label(new Rect(x + 190, 22, 180, 30), who, new GUIStyle(ui.Big) { fontSize = 20 });
                GUI.Label(new Rect(x + 360, 26, 200, 26), "経過 " + UiKit.FormatClock(game.ElapsedSeconds), ui.Clock);
            }
            float bx = vw - 14 - 4 * 128;
            var research = new Rect(bx, 16, 120, 40);
            ResearchButtonRect = new Rect(research.x * scale, research.y * scale, research.width * scale, research.height * scale);
            UiKit.Spot("play.research", research);
            UiKit.Spot("play.help", new Rect(bx + 128, 16, 120, 40));
            UiKit.Spot("play.settings", new Rect(bx + 256, 16, 120, 40));
            if (GUI.Button(research, "研究モードへ", ui.Button)) presentation.SetMode(PresentationMode.Research);
            if (GUI.Button(new Rect(bx + 128, 16, 120, 40), "あそびかた", ui.Button)) presentation.OpenHelp();
            if (GUI.Button(new Rect(bx + 256, 16, 120, 40), "設定", ui.Button)) { presentation.SettingsOpen = !presentation.SettingsOpen; confirmNew = false; }
            if (GUI.Button(new Rect(bx + 384, 16, 120, 40), "新規対局", ui.Button))
            {
                if (game.Phase == Phase.Setup || game.IsFinished) game.NewSetup(true);
                else { confirmNew = true; presentation.SettingsOpen = false; }
            }
        }

        private void DrawStatusBar()
        {
            var r = Region(new Rect(0, vh - 34, vw, 34));
            ui.Fill(r, new Color(0.04f, 0.03f, 0.02f, 0.75f));
            string text = game.Phase == Phase.Setup ? "自軍の駒をクリック → 置きたいマスをクリックで入れ替え。準備ができたら「対局開始」。"
                : game.Phase == Phase.PlayerTurn ? (game.SelectedNode >= 0 ? "移動先を選んでください（緑：移動　赤：攻撃　右クリックで取り消し）" : "あなたの番です。動かす駒をクリックしてください。")
                : game.Phase == Phase.CpuThinking ? "CPUが考えています…"
                : game.Phase == Phase.Finished ? game.ResultText() : "";
            GUI.Label(new Rect(16, vh - 30, vw - 32, 28), text, new GUIStyle(ui.Label) { alignment = TextAnchor.MiddleCenter });
        }

        /// <summary>Small headings over the loss areas: count only.</summary>
        private void DrawLossHeadings()
        {
            var cam = game.MainCamera;
            if (cam == null || game.Phase == Phase.Setup) return;
            // Count what the loss areas show (they fill in when a combat animation ends).
            int ownLost = game.Graveyard.OwnViews.Count;
            int enemyLost = game.Graveyard.EnemyViews.Count;
            for (int i = 0; i < 2; i++)
            {
                bool enemy = i == 1;
                var sp = cam.WorldToScreenPoint(GraveyardView.HeadingAnchor(enemy));
                if (sp.z < 0) continue;
                var r = new Rect(sp.x / scale - 90, (Screen.height - sp.y) / scale - 14, 180, 28);
                GUI.Label(r, (enemy ? "敵軍の損失　" : "自軍の損失　") + (enemy ? enemyLost : ownLost) + " / 31", ui.Heading);
                HeadingScreenRects[i] = new Rect(r.x * scale, r.y * scale, r.width * scale, r.height * scale);
            }
        }

        /// <summary>Before the game, three separate groups: CPU設定 / 自軍の配置 / 対局開始.</summary>
        private void DrawSetup()
        {
            float w = 316, h = Mathf.Min(vh - 88 - 44, 736);
            var r = Region(new Rect(vw - w - 16, 84, w, h));
            GUILayout.BeginArea(r, ui.Panel);

            GUILayout.Label("CPU設定", ui.Header);
            GUILayout.Label("CPUの強さ", ui.Small);
            GUILayout.BeginHorizontal();
            foreach (CpuStrength s in new[] { CpuStrength.Weak, CpuStrength.Normal, CpuStrength.Strong })
                if (GUILayout.Button(CpuProfile.StrengthName(s), game.Settings.Strength == s ? ui.Selected : ui.Button, GUILayout.Height(32)) && game.Settings.Strength != s)
                {
                    game.Settings.Strength = s;
                    game.RefreshCpu();
                }
            UiKit.SpotLast("setup.strongest");
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            GUILayout.Label("CPUの戦い方", ui.Small);
            for (int t = 2; t >= -2; t--)
                if (GUILayout.Button(CpuProfile.TemperamentName(t), game.Settings.Temperament == t ? ui.Selected : ui.Button, GUILayout.Height(28)) && game.Settings.Temperament != t)
                {
                    game.Settings.Temperament = t;
                    game.RefreshCpu();
                }

            UiKit.Separator(ui);
            GUILayout.Label("自軍の配置", ui.Header);
            GUILayout.Label("あなたの駒の並べ方（CPUの設定とは別です）", ui.Small);
            if (GUILayout.Button("おまかせ配置（新しい配置にする）", ui.Button, GUILayout.Height(32)))
            {
                game.Settings.PlayerFormationSeed = Random.Range(1, 1000000);
                game.AutoArrange();
            }
            UiKit.SpotLast("setup.omakase");
            GUILayout.Space(6);
            presets.Draw(ui, ui.TextField);

            UiKit.Separator(ui);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("対局開始", ui.BigButton, GUILayout.Height(48))) game.StartGame();
            GUILayout.EndArea();
        }

        private void DrawSettings()
        {
            var r = Region(new Rect(vw - 14 - 2 * 128 - SettingsPanel.Width / 2 + 60, 78, SettingsPanel.Width, SettingsPanel.Height));
            GUI.Box(r, GUIContent.none, ui.Panel);
            GUILayout.BeginArea(new Rect(r.x + 14, r.y + 10, r.width - 28, r.height - 20));
            if (settings.Draw(ui)) presentation.SettingsOpen = false;
            GUILayout.EndArea();
        }

        private void DrawConfirmNew()
        {
            var r = Region(new Rect(vw / 2 - 190, vh / 2 - 70, 380, 140));
            GUI.Box(r, GUIContent.none, ui.Panel);
            GUI.Label(new Rect(r.x, r.y + 18, r.width, 30), "この対局を終えて新しく始めますか？", new GUIStyle(ui.Label) { alignment = TextAnchor.MiddleCenter });
            if (GUI.Button(new Rect(r.x + 40, r.y + 76, 140, 40), "新規対局", ui.Button)) { confirmNew = false; game.NewSetup(true); }
            if (GUI.Button(new Rect(r.x + 200, r.y + 76, 140, 40), "続ける", ui.Button)) confirmNew = false;
        }

        private void DrawResult()
        {
            if (Event.current.type == EventType.Repaint) ResultDrawCount++;
            var r = Region(new Rect(vw / 2 - 230, vh / 2 - 95, 460, 190));
            GUI.Box(r, GUIContent.none, ui.Panel);
            GUI.Label(new Rect(r.x, r.y + 16, r.width, 44), game.ResultText(), new GUIStyle(ui.Big) { alignment = TextAnchor.MiddleCenter });
            LastResultText = "TURN " + game.Ply + "　経過 " + UiKit.FormatClock(game.ElapsedSeconds);
            GUI.Label(new Rect(r.x, r.y + 70, r.width, 30), LastResultText, new GUIStyle(ui.Label) { alignment = TextAnchor.MiddleCenter });
            if (GUI.Button(new Rect(r.x + r.width / 2 - 80, r.y + 124, 160, 40), "新規対局", ui.Button)) game.NewSetup(true);
        }
    }
}
