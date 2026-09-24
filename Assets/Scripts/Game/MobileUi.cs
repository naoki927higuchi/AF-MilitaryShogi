using System.Linq;
using MilitaryShogi.Cpu;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Android presentation of 対戦モード (there is no research mode on Android). Same game code as PC
    /// (GameController / GameSession / CPU / rules); only the layout and input differ:
    ///  - units are density-independent (UiKit.Scale = dpi / 160) and everything stays inside
    ///    Screen.safeArea (notch, punch hole, navigation bar);
    ///  - Portrait: top bar (title, TURN, 手番, 経過) / 敵軍の損失 (compact 2D rows, back faces, removal
    ///    order) / board / 自軍の損失 (compact 2D rows, faces, sorted by kind) / あそびかた・設定・新規対局;
    ///  - Landscape: like the PC screen – top bar with the buttons, board with the 3D loss tables left
    ///    and right;
    ///  - rotating only re-lays out the presentation: the session, CPU, seeds and clock are untouched;
    ///  - tap own piece → tap target; tap an enemy piece to see its public observations (no hover);
    ///  - all dialogs are ModalInput modals, so taps never go through them.
    /// </summary>
    public sealed class MobileUi : MonoBehaviour
    {
        private GameController game;
        private Presentation presentation;
        private UiKit ui;
        private float scale;
        private Rect safe;                 // safe area in UI units (top-left origin)
        private bool confirmNew;
        private bool sheetCollapsed;
        private Texture2D logo;
        private SettingsPanel settings;
        private PresetPanel presets;
        private readonly TouchScroll sheetScroll = new TouchScroll();
        private readonly TouchScroll settingsScroll = new TouchScroll();
        private float sheetContentHeight = 560f;
        private bool wasSaving;
        private int scrollToEnd;

        public const float Touch = 48f;    // minimum tap height (dp)

        public bool Portrait { get; private set; }
        public Rect SafeArea { get { return safe; } }
        public int LastDrawFrame { get; private set; } = -1;
        /// <summary>Placement instructions drawn under the portrait 「初期配置」 title at the last repaint ("" = none), for the device test.</summary>
        public string SetupHint { get; private set; } = "";
        public string LastResultText { get; private set; }
        public PresetPanel Presets { get { return presets; } }
        public bool ConfirmNewOpen { get { return confirmNew; } set { confirmNew = value; } }
        public bool SheetCollapsed { get { return sheetCollapsed; } set { sheetCollapsed = value; } }

        public void Bind(GameController controller, Presentation presentation)
        {
            game = controller;
            this.presentation = presentation;
            logo = GameAssets.Texture("UI/title_logo");
            settings = new SettingsPanel(controller.Settings) { Row = 44f, Touch = true };
            presets = new PresetPanel(controller) { Row = 44f };
            controller.SetupStarted += () => { presets.Saving = false; presets.Message = ""; confirmNew = false; sheetScroll.Offset = 0; };
            ModalInput.Register("confirmNew", 50, () => confirmNew);
        }

        // ------------------------------------------------------------------
        // Layout (every frame, before Presentation.LateUpdate fits the camera)
        // ------------------------------------------------------------------

        /// <summary>Portrait after the game: one more row for the 棋譜再現 controls (finger-sized buttons).</summary>
        private float TopBarHeight { get { return Portrait ? (game != null && game.Phase == Phase.Finished ? 96f : 66f) : 54f; } }
        private float BottomBarHeight { get { return Portrait ? 84f : 26f; } }
        private const int StripColumns = 12, StripRows = 3;
        private float StripIcon { get { return (safe.width - 16f) / StripColumns; } }
        private float StripHeight { get { return 22f + StripRows * (StripIcon * 1.12f + 2f); } }
        private float SheetHeight
        {
            get
            {
                if (Portrait) return sheetCollapsed ? 118f : Mathf.Min(sheetContentHeight + 12f, safe.height * 0.58f);
                return 0f;
            }
        }
        private const float SideSheetWidth = 340f;

        private void Update()
        {
            if (game == null) return;
            scale = UiKit.Scale;
            var sa = Screen.safeArea;
            safe = new Rect(sa.x / scale, (Screen.height - sa.yMax) / scale, sa.width / scale, sa.height / scale);
            Portrait = Screen.height >= Screen.width;

            bool setup = game.Phase == Phase.Setup;
            // Starting to save a preset: bring the name field and 保存/やめる into view.
            // (a few frames: the sheet's content height grows once the name row has been laid out)
            if (presets.Saving && !wasSaving) scrollToEnd = 4;
            wasSaving = presets.Saving;
            if (scrollToEnd > 0) { scrollToEnd--; sheetScroll.Offset = float.MaxValue; }
            Rect board;
            if (Portrait)
            {
                float top = safe.y + TopBarHeight;
                float bottom = safe.yMax - (setup ? SheetHeight : BottomBarHeight);
                if (!setup) { top += StripHeight; bottom -= StripHeight; }
                board = Rect.MinMaxRect(safe.x, top, safe.xMax, bottom);
            }
            else
            {
                float right = setup ? safe.xMax - SideSheetWidth - 8f : safe.xMax;
                board = Rect.MinMaxRect(safe.x, safe.y + TopBarHeight, right, safe.yMax - BottomBarHeight);
            }
            presentation.HideGraveyards3D = Portrait || setup;
            presentation.PlayAreaOverride = new Rect(board.x * scale, board.y * scale, board.width * scale, board.height * scale);

            // Android back key: closes the frontmost dialog (help and the referee notice handle their own).
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                string top = ModalInput.Top;
                if (top == "settings") presentation.SettingsOpen = false;
                else if (top == "confirmNew") confirmNew = false;
                else if (top == "result") presentation.ResultDismissed = true;
                else if (top == null && presets.Saving) presets.Saving = false;
                else if (top == null && game.InspectedPieceId >= 0) game.ClearInspection();
            }
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (game == null) return;
            UiGuard.Run("MobileUi", DrawGui, () => { confirmNew = false; presentation.SettingsOpen = false; presets.Saving = false; });
        }

        private Rect Region(Rect r)
        {
            game.UiRects.Add(new Rect(r.x * scale, r.y * scale, r.width * scale, r.height * scale));
            return r;
        }

        private void DrawGui()
        {
            if (ui == null) ui = new UiKit();
            if (Event.current.type == EventType.Repaint) { LastDrawFrame = Time.frameCount; SetupHint = ""; }
            scale = UiKit.Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            game.UiRects.Clear();

            string top = ModalInput.Top;
            using (ModalInput.Background())
            {
                DrawTopBar();
                if (Portrait)
                {
                    if (game.Phase != Phase.Setup) { DrawStrips(); DrawBottomBar(); }
                    else DrawPortraitSheet();
                }
                else
                {
                    DrawLossHeadings();
                    DrawStatusLine();
                    if (game.Phase == Phase.Setup) DrawSideSheet();
                }
            }
            game.Tooltip.Draw(top != null, false, game.Settings.ObservationTooltip);
            if (presentation.ResultOpen)
                using (ModalInput.Background(top != "result")) DrawResult();
            if (presentation.SettingsOpen)
                using (ModalInput.Background(top != "settings")) DrawSettings();
            if (confirmNew)
                using (ModalInput.Background(top != "confirmNew")) DrawConfirmNew();
        }

        private GUIStyle Center(GUIStyle s) { return new GUIStyle(s) { alignment = TextAnchor.MiddleCenter }; }

        private bool Button(Rect r, string text, string spot, GUIStyle style = null)
        {
            UiKit.Spot(spot, r);
            return GUI.Button(r, text, style ?? ui.Button);
        }

        private void HeaderButtons(Rect area)
        {
            // あそびかた / 設定 / 新規対局 – equal widths, at least 48 units tall.
            float gap = 8f, w = (area.width - 2 * gap) / 3f;
            if (Button(new Rect(area.x, area.y, w, area.height), "あそびかた", "mobile.help")) presentation.OpenHelp();
            if (Button(new Rect(area.x + w + gap, area.y, w, area.height), "設定", "mobile.settings")) { presentation.SettingsOpen = true; confirmNew = false; }
            if (Button(new Rect(area.x + 2 * (w + gap), area.y, w, area.height), "新規対局", "mobile.new"))
            {
                if (game.Phase == Phase.Setup || game.IsFinished) game.NewSetup(true);
                else confirmNew = true;
            }
        }

        private void DrawTopBar()
        {
            var bar = Region(new Rect(safe.x, safe.y, safe.width, TopBarHeight));
            ui.Fill(new Rect(0, 0, Screen.width / scale, safe.y + TopBarHeight), new Color(0.04f, 0.03f, 0.02f, 0.9f));
            ui.Fill(new Rect(bar.x, bar.yMax - 1, bar.width, 1), new Color(0.75f, 0.55f, 0.28f, 0.6f));
            // Portrait setup: logo, 「初期配置」 and the placement instructions stacked in the same bar height
            // (the board area does not change; the instructions stay while the sheet is folded).
            bool setupHint = Portrait && game.Phase == Phase.Setup;
            float lh = setupHint ? 27f : Portrait ? 30f : 38f;
            float lw = logo != null ? lh * logo.width / logo.height : 0f;
            if (logo != null) GUI.DrawTexture(new Rect(bar.x + 8, bar.y + (setupHint ? 3f : 4f), lw, lh), logo, ScaleMode.ScaleToFit, true);
            string turn = game.Phase == Phase.Setup ? "初期配置" : "TURN " + (game.Ply + (game.IsFinished ? 0 : 1));
            bool mine = game.ToMove == GameController.Human;
            string who = game.Phase == Phase.Setup ? "" : game.IsFinished ? "終局" : mine ? "あなたの番" : "CPUの番";
            Color dot = game.IsFinished ? Color.gray : mine ? new Color(0.45f, 0.9f, 0.5f) : new Color(0.95f, 0.55f, 0.35f);
            string clock = game.Session.Started ? "経過 " + UiKit.FormatClock(game.ElapsedSeconds) : "";
            var big = new GUIStyle(ui.Big) { fontSize = 20 };
            bool review = game.Phase == Phase.Finished;
            if (Portrait && review)
            {
                // 棋譜再現: 「敵駒開示」 beside the logo, ◀◀ ◀ TURN n / N ▶ ▶▶ on the second row.
                const float tw = 128f;
                GUI.Label(new Rect(bar.x + 14 + lw, bar.y + 8, bar.width - lw - tw - 30, 24), clock, ui.Clock);
                var row = new Rect(bar.x + 8, bar.y + 42, bar.width - 16, 48);
                ReplayBar.Draw(ui, game, new Rect(row.x, row.y, row.width, row.height), 0f, 18);
                var toggle = new Rect(bar.xMax - tw - 8, bar.y + 4, tw, 34);
                UiKit.Spot("replay.reveal", toggle);
                bool on = game.PostGameReveal;
                if (GUI.Button(toggle, on ? "敵駒開示 ON" : "敵駒開示 OFF", on ? ui.Selected : ui.Button)) game.SetPostGameReveal(!on);
            }
            else if (setupHint)
            {
                GUI.Label(new Rect(bar.x + 10, bar.y + 29, 200, 22), turn, new GUIStyle(ui.Big) { fontSize = 17 });
                var hint = new Rect(bar.x + 10, bar.y + 50, bar.width - 20, 16);
                string text = StatusText();
                GUI.Label(hint, text, new GUIStyle(ui.Small) { fontSize = 12, wordWrap = false, clipping = TextClipping.Clip });
                UiKit.Spot("mobile.setupHint", hint);
                if (Event.current.type == EventType.Repaint) SetupHint = text;
            }
            else if (Portrait)
            {
                GUI.Label(new Rect(bar.xMax - 130, bar.y + 6, 122, 24), clock, new GUIStyle(ui.Clock) { alignment = TextAnchor.MiddleRight });
                float y = bar.y + 36;
                GUI.Label(new Rect(bar.x + 10, y, 150, 26), turn, big);
                if (who != "")
                {
                    ui.Fill(new Rect(bar.x + 150, y + 8, 11, 11), dot);
                    GUI.Label(new Rect(bar.x + 166, y, 180, 26), who, big);
                }
            }
            else if (review)
            {
                // Landscape: the controls take the place of TURN / 手番 / 経過; the header buttons get narrower.
                float x = bar.x + 16 + lw;
                float bw = Mathf.Min(3 * 96f + 16f, bar.width * 0.34f);
                ReplayBar.Draw(ui, game, new Rect(x, bar.y + 4, bar.xMax - bw - 14 - x, TopBarHeight - 8), 112f, 17);
                HeaderButtons(new Rect(bar.xMax - bw - 6, bar.y + 4, bw, TopBarHeight - 8));
            }
            else
            {
                float x = bar.x + 16 + lw;
                GUI.Label(new Rect(x, bar.y + 12, 130, 30), turn, big);
                if (who != "")
                {
                    ui.Fill(new Rect(x + 130, bar.y + 22, 11, 11), dot);
                    GUI.Label(new Rect(x + 146, bar.y + 12, 130, 30), who, big);
                }
                GUI.Label(new Rect(x + 280, bar.y + 16, 110, 24), clock, ui.Clock);
                float bw = Mathf.Min(3 * 118f + 16f, bar.width - (x + 390 - bar.x));
                HeaderButtons(new Rect(bar.xMax - bw - 6, bar.y + 4, bw, TopBarHeight - 8));
            }
        }

        private string StatusText()
        {
            return game.Phase == Phase.Setup ? "自軍の駒をタップ → 置きたいマスをタップで入れ替え"
                : game.Phase == Phase.PlayerTurn ? (game.SelectedNode >= 0 ? "移動先をタップ（緑：移動　赤：攻撃）" : "動かす駒をタップ。敵駒をタップすると観測情報")
                : game.Phase == Phase.CpuThinking ? "CPUが考えています…"
                : game.Phase == Phase.Finished ? game.ResultText() : game.StatusText;
        }

        private void DrawStatusLine()
        {
            var r = Region(new Rect(safe.x, safe.yMax - BottomBarHeight, safe.width, BottomBarHeight));
            ui.Fill(r, new Color(0.04f, 0.03f, 0.02f, 0.7f));
            GUI.Label(r, StatusText(), Center(ui.Small));
        }

        private void DrawBottomBar()
        {
            var r = Region(new Rect(safe.x, safe.yMax - BottomBarHeight, safe.width, BottomBarHeight));
            ui.Fill(new Rect(0, r.y, Screen.width / scale, Screen.height / scale - r.y), new Color(0.04f, 0.03f, 0.02f, 0.9f));
            GUI.Label(new Rect(r.x + 8, r.y + 2, r.width - 16, 22), StatusText(), Center(ui.Small));
            HeaderButtons(new Rect(r.x + 8, r.y + 26, r.width - 16, 52));
        }

        // ------------------------------------------------------------------
        // Portrait losses: compact rows (own: faces sorted by kind; enemy: backs in removal order)
        // ------------------------------------------------------------------

        private void DrawStrips()
        {
            float top = safe.y + TopBarHeight;
            float bottom = safe.yMax - BottomBarHeight - StripHeight;
            var g = game.Graveyard;
            // Backs in removal order; faces only with 「敵駒開示」 after the game (order unchanged).
            DrawStrip(new Rect(safe.x, top, safe.width, StripHeight), "敵軍の損失", g.EnemyViews.Count, i =>
            {
                var kind = game.PostGameReveal ? game.Session.ResearchTrueKind(g.EnemyIdsShown[i]) : null;
                return kind.HasValue ? GameAssets.Face(kind.Value) : GameAssets.Back;
            });
            DrawStrip(new Rect(safe.x, bottom, safe.width, StripHeight), "自軍の損失", g.OwnViews.Count, i => GameAssets.Face(game.View.OwnById(g.OwnViews[i].Id).Type));
        }

        private void DrawStrip(Rect r, string title, int count, System.Func<int, Texture2D> texture)
        {
            ui.Fill(r, new Color(0.05f, 0.04f, 0.03f, 0.55f));
            GUI.Label(new Rect(r.x + 8, r.y + 1, r.width - 16, 20), title + "　" + count + " / 31", ui.Heading);
            float w = StripIcon, h = w * 1.12f;
            for (int i = 0; i < count && i < StripColumns * StripRows; i++)
            {
                int row = i / StripColumns, col = i % StripColumns;
                var c = new Rect(r.x + 8 + col * w, r.y + 22 + row * (h + 2), w - 1, h);
                var tex = texture(i);
                if (tex != null) GUI.DrawTexture(c, tex, ScaleMode.ScaleToFit, true);
            }
        }

        private void DrawLossHeadings()
        {
            var cam = game.MainCamera;
            if (cam == null || game.Phase == Phase.Setup || !presentation.GraveyardsVisible) return;
            for (int i = 0; i < 2; i++)
            {
                bool enemy = i == 1;
                var sp = cam.WorldToScreenPoint(GraveyardView.HeadingAnchor(enemy));
                if (sp.z < 0) continue;
                int n = enemy ? game.Graveyard.EnemyViews.Count : game.Graveyard.OwnViews.Count;
                // Short form: on a phone in landscape the loss tables are narrow.
                GUI.Label(new Rect(sp.x / scale - 60, (Screen.height - sp.y) / scale - 12, 120, 24), (enemy ? "敵軍 " : "自軍 ") + n + "/31", new GUIStyle(ui.Heading) { fontSize = 13 });
            }
        }

        // ------------------------------------------------------------------
        // Setup (CPU settings, own placement, presets, start)
        // ------------------------------------------------------------------

        private void DrawPortraitSheet()
        {
            var r = Region(new Rect(safe.x, safe.yMax - SheetHeight, safe.width, SheetHeight));
            ui.Fill(new Rect(0, r.y, Screen.width / scale, Screen.height / scale - r.y), new Color(0.06f, 0.045f, 0.035f, 0.95f));
            // Fixed row: collapse/expand, あそびかた・設定, 対局開始.
            float y = r.y + 6;
            float half = (r.width - 24) / 2f;
            if (Button(new Rect(r.x + 8, y, half, Touch), sheetCollapsed ? "▲ 設定・配置を開く" : "▼ 盤を広く表示", "mobile.sheet")) sheetCollapsed = !sheetCollapsed;
            if (Button(new Rect(r.x + 16 + half, y, half, Touch), "対局開始", "mobile.start", ui.BigButton)) game.StartGame();
            y += Touch + 6;
            if (sheetCollapsed)
            {
                HeaderButtons(new Rect(r.x + 8, y, r.width - 16, Touch));
                return;
            }
            var view = new Rect(r.x + 8, y, r.width - 16, r.yMax - y - 4);
            sheetScroll.Begin(view, sheetContentHeight);
            GUILayout.BeginArea(new Rect(0, 0, view.width, sheetContentHeight));
            HeaderButtonsLayout();
            SetupContent(view.width);
            GUILayout.EndArea();
            sheetScroll.End();
        }

        private void HeaderButtonsLayout()
        {
            var r = GUILayoutUtility.GetRect(10, Touch, GUILayout.ExpandWidth(true));
            HeaderButtons(r);
            GUILayout.Space(6);
        }

        private void DrawSideSheet()
        {
            var r = Region(new Rect(safe.xMax - SideSheetWidth, safe.y + TopBarHeight + 4, SideSheetWidth, safe.height - TopBarHeight - BottomBarHeight - 8));
            GUI.Box(r, GUIContent.none, ui.Panel);
            float y = r.y + 8;
            if (Button(new Rect(r.x + 10, r.yMax - Touch - 10, r.width - 20, Touch), "対局開始", "mobile.start", ui.BigButton)) game.StartGame();
            var view = new Rect(r.x + 10, y, r.width - 20, r.yMax - y - Touch - 18);
            sheetScroll.Begin(view, sheetContentHeight);
            GUILayout.BeginArea(new Rect(0, 0, view.width, sheetContentHeight));
            SetupContent(view.width);
            GUILayout.EndArea();
            sheetScroll.End();
        }

        private void SetupContent(float width)
        {
            GUILayout.Label("CPU設定", ui.Header);
            GUILayout.Label("CPUの強さ", ui.Small);
            GUILayout.BeginHorizontal();
            foreach (CpuStrength s in new[] { CpuStrength.Weak, CpuStrength.Normal, CpuStrength.Strong })
                if (GUILayout.Button(CpuProfile.StrengthName(s), game.Settings.Strength == s ? ui.Selected : ui.Button, GUILayout.Height(Touch)) && game.Settings.Strength != s)
                {
                    game.Settings.Strength = s;
                    game.RefreshCpu();
                }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            GUILayout.Label("CPUの戦い方", ui.Small);
            GUILayout.BeginHorizontal();
            int t = game.Settings.Temperament;
            if (GUILayout.Button("◀", ui.Button, GUILayout.Width(Touch + 8), GUILayout.Height(Touch)) && t < 2) { game.Settings.Temperament = t + 1; game.RefreshCpu(); }
            GUILayout.Label(CpuProfile.TemperamentName(game.Settings.Temperament), new GUIStyle(ui.Label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }, GUILayout.Height(Touch), GUILayout.ExpandWidth(true));
            if (GUILayout.Button("▶", ui.Button, GUILayout.Width(Touch + 8), GUILayout.Height(Touch)) && t > -2) { game.Settings.Temperament = t - 1; game.RefreshCpu(); }
            GUILayout.EndHorizontal();
            GUILayout.Label("◀ 攻撃的　　　防御的 ▶", new GUIStyle(ui.Small) { alignment = TextAnchor.MiddleCenter });
            UiKit.Separator(ui);
            GUILayout.Label("自軍の配置", ui.Header);
            if (GUILayout.Button("おまかせ配置（新しい配置にする）", ui.Button, GUILayout.Height(Touch)))
            {
                game.Settings.PlayerFormationSeed = Random.Range(1, 1000000);
                game.AutoArrange();
            }
            UiKit.SpotLast("mobile.omakase");
            GUILayout.Space(6);
            presets.Draw(ui, new GUIStyle(ui.TextField) { fontSize = 17 });
            GUILayout.Space(8);
            var end = GUILayoutUtility.GetRect(1, 1);
            if (Event.current.type == EventType.Repaint && end.y > 50) sheetContentHeight = end.y + 8;
        }

        // ------------------------------------------------------------------
        // Dialogs
        // ------------------------------------------------------------------

        private Rect Dialog(float w, float h)
        {
            w = Mathf.Min(w, safe.width - 16);
            h = Mathf.Min(h, safe.height - 16);
            var r = new Rect(safe.x + (safe.width - w) / 2, safe.y + (safe.height - h) / 2, w, h);
            ui.Fill(new Rect(0, 0, Screen.width / scale, Screen.height / scale), new Color(0, 0, 0, 0.55f));
            ui.Fill(r, new Color(0.07f, 0.05f, 0.04f, 1f));   // opaque: nothing of the screen behind shows through
            return Region(r);
        }

        private void DrawSettings()
        {
            var r = Dialog(400, settings.PreferredHeight + 20);
            GUI.Box(r, GUIContent.none, ui.Panel);
            // Fixed 閉じる in the corner: in landscape the panel scrolls and its own 閉じる may be out of view.
            if (Button(new Rect(r.xMax - 104, r.y + 6, 96, 44), "閉じる", "mobile.settings.close")) presentation.SettingsOpen = false;
            var view = new Rect(r.x + 14, r.y + 10, r.width - 28, r.height - 20);
            settingsScroll.Begin(view, settings.PreferredHeight);
            GUILayout.BeginArea(new Rect(0, 0, view.width, settings.PreferredHeight));
            if (settings.Draw(ui)) presentation.SettingsOpen = false;
            GUILayout.EndArea();
            settingsScroll.End();
        }

        private void DrawConfirmNew()
        {
            var r = Dialog(380, 150);
            GUI.Box(r, GUIContent.none, ui.Panel);
            GUI.Label(new Rect(r.x + 10, r.y + 18, r.width - 20, 30), "この対局を終えて新しく始めますか？", Center(ui.Label));
            float bw = (r.width - 60) / 2;
            if (Button(new Rect(r.x + 20, r.yMax - 66, bw, 52), "新規対局", "mobile.confirm.new")) { confirmNew = false; game.NewSetup(true); }
            if (Button(new Rect(r.x + 40 + bw, r.yMax - 66, bw, 52), "続ける", "mobile.confirm.keep")) confirmNew = false;
        }

        private void DrawResult()
        {
            var r = Dialog(440, 250);
            GUI.Box(r, GUIContent.none, ui.Panel);
            GUI.Label(new Rect(r.x, r.y + 16, r.width, 40), game.ResultTitle(), Center(ui.Big));
            GUI.Label(new Rect(r.x + 12, r.y + 60, r.width - 24, 48), game.ResultReason(), new GUIStyle(ui.Label) { alignment = TextAnchor.UpperCenter });
            string clock = "TURN " + game.Ply + "　経過 " + UiKit.FormatClock(game.ElapsedSeconds);
            LastResultText = game.ResultText() + "　" + clock;
            GUI.Label(new Rect(r.x, r.y + 112, r.width, 26), clock, Center(ui.Label));
            float bw = (r.width - 60) / 2;
            if (Button(new Rect(r.x + 20, r.yMax - 70, bw, 52), "盤面を見る", "mobile.result.board")) presentation.ResultDismissed = true;
            if (Button(new Rect(r.x + 40 + bw, r.yMax - 70, bw, 52), "新規対局", "mobile.result.new")) game.NewSetup(true);
        }
    }
}
