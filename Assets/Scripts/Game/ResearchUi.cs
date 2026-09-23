using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MilitaryShogi.Cpu;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// 研究モード (research mode, 1.0.0 UI). Drawn only while <see cref="Presentation.Mode"/> is Research.
    /// IMGUI front end: top bar, setup panel, battle history, enemy info on hover and the
    /// CPU thinking monitor (research view). Everything about the human's opponent shown
    /// here comes from the human's PlayerView (positions, history, results) – never from the
    /// referee. The monitor shows the CPU's own reasoning.
    /// </summary>
    public sealed class ResearchUi : MonoBehaviour
    {
        private const float VirtualHeight = 900f;
        private GameController game;
        private float scale = 1f;
        private float vw, vh;
        public const float LeftColumn = 280f;
        private bool showHistory;
        private bool showOwnTruth = true;
        private int monitorTab;
        private int reportIndex = -1;   // -1 = latest
        private int selectedEnemy = -1; // CPU-side belief selection (a human piece id)
        private Vector2 candScroll, beliefScroll, noteScroll, historyScroll, reasonScroll;
        private string pSeed, cSeed, dSeed;
        private GUIStyle panel, label, small, header, title, button, toggle, cell, cellRight, barBack, chosenRow, textField, popup, tagStyle;
        private Texture2D panelTex, rowTex, barTex, bar2Tex, barBgTex, chosenTex;
        public string Version = "1.0.0";

        private Presentation presentation;
        private UiKit kit;
        private SettingsPanel settingsPanel;
        private PresetPanel presets;
        public int LastDrawFrame { get; private set; } = -1;
        public string LastResultText { get; private set; }
        public PresetPanel Presets { get { return presets; } }

        // Monitor visibility and the reveal switch are presentation state shared with the board/tooltip.
        private bool showMonitor { get { return presentation.MonitorOpen; } set { presentation.MonitorOpen = value; } }
        private bool spoiler { get { return presentation.RevealCpuPieces; } set { presentation.RevealCpuPieces = value; } }

        public void Bind(GameController controller, Presentation presentation)
        {
            game = controller;
            this.presentation = presentation;
            presentation.ModeChanged += m => SyncSeedFields();   // show the running game's seeds when entering research
            SyncSeedFields();
            settingsPanel = new SettingsPanel(controller.Settings);
            presets = new PresetPanel(controller);
            game.GameStarted += () => { reportIndex = -1; selectedEnemy = -1; };
            // 1.2.0 fix: a past-turn index must not survive into a new setup (no reports exist there).
            game.SetupStarted += () => { reportIndex = -1; selectedEnemy = -1; presets.Saving = false; presets.Message = ""; SyncSeedFields(); };
        }

        private void SyncSeedFields()
        {
            pSeed = game.Settings.PlayerFormationSeed.ToString();
            cSeed = game.Settings.CpuFormationSeed.ToString();
            dSeed = game.Settings.CpuDecisionSeed.ToString();
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void EnsureStyles()
        {
            if (panel != null) return;
            var font = GameAssets.UiFont;
            panelTex = Solid(new Color(0.07f, 0.05f, 0.04f, 0.9f));
            rowTex = Solid(new Color(1f, 1f, 1f, 0.05f));
            chosenTex = Solid(new Color(1f, 0.8f, 0.3f, 0.22f));
            barTex = Solid(new Color(0.95f, 0.7f, 0.3f, 0.95f));
            bar2Tex = Solid(new Color(0.55f, 0.8f, 1f, 0.95f));
            barBgTex = Solid(new Color(1f, 1f, 1f, 0.08f));
            panel = new GUIStyle { normal = { background = panelTex }, padding = new RectOffset(10, 10, 8, 8) };
            label = new GUIStyle { font = font, fontSize = 14, wordWrap = true, richText = true, normal = { textColor = new Color(0.93f, 0.9f, 0.85f) } };
            small = new GUIStyle(label) { fontSize = 12 };
            header = new GUIStyle(label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.55f) } };
            title = new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = false, normal = { textColor = new Color(1f, 0.9f, 0.7f) } };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 13 };
            toggle = new GUIStyle(GUI.skin.toggle) { font = font, fontSize = 13, normal = { textColor = label.normal.textColor }, onNormal = { textColor = label.normal.textColor } };
            textField = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 14 };
            cell = new GUIStyle(small) { wordWrap = false, clipping = TextClipping.Clip };
            cellRight = new GUIStyle(cell) { alignment = TextAnchor.UpperRight };
            barBack = new GUIStyle { normal = { background = barBgTex } };
            chosenRow = new GUIStyle { normal = { background = chosenTex } };
            popup = new GUIStyle(label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            tagStyle = new GUIStyle(small) { fontSize = 10, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { background = Solid(new Color(0, 0, 0, 0.55f)), textColor = Color.white } };
        }

        private void Update()
        {
            if (game == null || presentation.Mode != PresentationMode.Research || presentation.HelpOpen) return;
            if (Input.GetKeyDown(KeyCode.M)) showMonitor = !showMonitor;
            if (Input.GetKeyDown(KeyCode.H)) showHistory = !showHistory;
            if (Input.GetKeyDown(KeyCode.F)) { game.Settings.Effect = game.Settings.Effect == EffectMode.Normal ? EffectMode.Simple : EffectMode.Normal; UserData.SaveSettings(game.Settings); }
            if (Input.GetKeyDown(KeyCode.N)) game.Settings.ShowEnemyNumbers = !game.Settings.ShowEnemyNumbers;
            // The board is drawn in the area not covered by the left column and the monitor.
            float s = Mathf.Clamp(Screen.height / VirtualHeight, 0.75f, 2.5f);
            float left = LeftColumn * s;
            float right = showMonitor ? Mathf.Min(700f, Screen.width / s * 0.44f) * s : 0f;
            presentation.ResearchBoardArea = new Rect(left, 44f * s, Mathf.Max(0.2f * Screen.width, Screen.width - left - right), Screen.height - 44f * s);
        }

        public bool MonitorVisible { get { return showMonitor; } set { showMonitor = value; } }
        public int ReportIndex { get { return reportIndex; } set { reportIndex = value; } }
        public int MonitorTab { get { return monitorTab; } set { monitorTab = value; } }
        public bool HistoryVisible { get { return showHistory; } set { showHistory = value; } }

        private float MonitorWidth() { return Mathf.Min(700f, (vw > 0 ? vw : Screen.width) * 0.44f); }

        private void OnGUI()
        {
            if (game == null || presentation.Mode != PresentationMode.Research) return;
            UiGuard.Run("ResearchUi", DrawGui, () => { reportIndex = -1; selectedEnemy = -1; presentation.SettingsOpen = false; presets.Saving = false; });
        }

        private void DrawGui()
        {
            EnsureStyles();
            if (kit == null) kit = new UiKit();
            if (Event.current.type == EventType.Repaint) LastDrawFrame = Time.frameCount;
            scale = Mathf.Clamp(Screen.height / VirtualHeight, 0.75f, 2.5f);
            vw = Screen.width / scale;
            vh = Screen.height / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            game.UiRects.Clear();

            DrawWorldLabels();
            DrawTopBar();
            if (game.Phase == Phase.Setup) DrawSetupPanel();
            else DrawInfoPanel();
            if (showHistory) DrawHistory();
            if (showMonitor) DrawMonitor();
            game.Tooltip.Draw(presentation.HelpOpen || presentation.SettingsOpen, presentation.RevealCpuPieces, true);
            if (game.Phase == Phase.Finished) DrawResult();
            if (presentation.SettingsOpen) DrawSettings();
        }

        private Rect Region(Rect r)
        {
            game.UiRects.Add(new Rect(r.x * scale, r.y * scale, r.width * scale, r.height * scale));
            return r;
        }

        // ------------------------------------------------------------------
        // Top bar
        // ------------------------------------------------------------------

        private void DrawTopBar()
        {
            var r = Region(new Rect(0, 0, vw, 44));
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(12, 8, 200, 30), "研究モード", title);
            GUI.Label(new Rect(124, 14, 160, 24), "AF-MilitaryShogi " + Version, small);
            string turn = game.Phase == Phase.Setup ? "初期配置" : "TURN " + (game.Ply + (game.IsFinished ? 0 : 1)) + "　手番: " + (game.IsFinished ? "終局" : game.ToMove == GameController.Human ? "あなた（手前）" : "CPU（奥）")
                + "　経過 " + UiKit.FormatClock(game.ElapsedSeconds);
            GUI.Label(new Rect(270, 12, 380, 24), turn, label);
            float x = vw - 552;
            GUI.Label(new Rect(650, 12, Mathf.Max(0, x - 300 - 650), 24), game.StatusText, small);
            if (GUI.Button(new Rect(x - 300, 8, 96, 28), "対戦モードへ", button)) presentation.SetMode(PresentationMode.Play);
            if (GUI.Button(new Rect(x - 200, 8, 96, 28), "あそびかた", button)) presentation.OpenHelp();
            if (GUI.Button(new Rect(x - 100, 8, 96, 28), "設定", button)) presentation.SettingsOpen = !presentation.SettingsOpen;
            if (GUI.Button(new Rect(x, 8, 118, 28), (showMonitor ? "■" : "□") + " 思考モニター(M)", button)) showMonitor = !showMonitor;
            if (GUI.Button(new Rect(x + 122, 8, 100, 28), (showHistory ? "■" : "□") + " 戦闘履歴(H)", button)) showHistory = !showHistory;
            if (GUI.Button(new Rect(x + 226, 8, 92, 28), "演出:" + (game.Settings.Effect == EffectMode.Normal ? "通常" : "簡易") + "(F)", button))
            {
                game.Settings.Effect = game.Settings.Effect == EffectMode.Normal ? EffectMode.Simple : EffectMode.Normal;
                UserData.SaveSettings(game.Settings);
            }
            if (GUI.Button(new Rect(x + 322, 8, 64, 28), "速度x" + game.Settings.EffectSpeed.ToString("0"), button))
            {
                game.Settings.EffectSpeed = game.Settings.EffectSpeed >= 4 ? 1 : game.Settings.EffectSpeed * 2;
                UserData.SaveSettings(game.Settings);
            }
            if (GUI.Button(new Rect(x + 390, 8, 72, 28), "番号(N)", button)) game.Settings.ShowEnemyNumbers = !game.Settings.ShowEnemyNumbers;
            if (GUI.Button(new Rect(x + 466, 8, 80, 28), "新規対局", button)) { game.NewSetup(false); SyncSeedFields(); }
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        private void DrawSetupPanel()
        {
            var r = Region(new Rect(8, 52, LeftColumn - 16, Mathf.Min(760, vh - 64)));
            GUILayout.BeginArea(r, panel);
            GUILayout.Label("自軍の配置", header);
            GUILayout.Space(4);
            // Seed is the single control here: 適用 = arrange from the entered seed, 乱数 = new seed and arrangement.
            SeedRow("Player Formation Seed", ref pSeed, v => { game.Settings.PlayerFormationSeed = v; game.AutoArrange(); });
            GUILayout.Label("おまかせ配置の思想: " + FormationStyles.JapaneseName(game.PlayerStyle), small);
            presets.Draw(kit, textField);
            GUILayout.Space(6);
            GUILayout.Label("CPU設定", header);
            SeedRow("CPU Formation Seed", ref cSeed, v => { game.Settings.CpuFormationSeed = v; game.RefreshCpu(); });
            SeedRow("CPU Decision Seed", ref dSeed, v => { game.Settings.CpuDecisionSeed = v; game.RefreshCpu(); });
            GUILayout.Space(6);
            GUILayout.Label("CPU配置思想（検証用に指定可）", small);
            var names = new List<string> { "自動（Seedで決定）" };
            names.AddRange(Enum.GetValues(typeof(FormationStyle)).Cast<FormationStyle>().Select(FormationStyles.JapaneseName));
            int current = game.Settings.CpuStyle.HasValue ? (int)game.Settings.CpuStyle.Value + 1 : 0;
            int chosen = GUILayout.SelectionGrid(current, names.ToArray(), 2, button);
            if (chosen != current)
            {
                game.Settings.CpuStyle = chosen == 0 ? (FormationStyle?)null : (FormationStyle)(chosen - 1);
                game.RefreshCpu();
            }
            GUILayout.Space(6);
            GUILayout.Label("CPUの強さ／戦い方（対戦モードと共通）", small);
            int strength = GUILayout.Toolbar((int)game.Settings.Strength, new[] { "弱", "中", "強" }, button);
            if (strength != (int)game.Settings.Strength) { game.Settings.Strength = (CpuStrength)strength; game.RefreshCpu(); }
            int temper = GUILayout.SelectionGrid(2 - game.Settings.Temperament, Enumerable.Range(0, 5).Select(i => CpuProfile.TemperamentName(2 - i)).ToArray(), 3, button);
            if (temper != 2 - game.Settings.Temperament) { game.Settings.Temperament = 2 - temper; game.RefreshCpu(); }
            GUILayout.Space(8);
            GUILayout.Label("自軍の駒をクリック → 移動先／交換先をクリック。青いマスだけが置けるマスです（地雷・軍旗は突入口に、軍旗は最後列に置けません）。", small);
            GUILayout.FlexibleSpace();
            GUILayout.Label("対局開始", header);
            if (GUILayout.Button("対局開始", button, GUILayout.Height(40))) game.StartGame();
            GUILayout.EndArea();
        }

        private void DrawSettings()
        {
            var r = Region(new Rect(vw - 552 - 100 - SettingsPanel.Width / 2, 48, SettingsPanel.Width, SettingsPanel.Height));
            GUI.Box(r, GUIContent.none, kit.Panel);
            GUILayout.BeginArea(new Rect(r.x + 14, r.y + 10, r.width - 28, r.height - 20));
            if (settingsPanel.Draw(kit)) presentation.SettingsOpen = false;
            GUILayout.EndArea();
        }

        private void SeedRow(string name, ref string field, Action<int> apply)
        {
            GUILayout.Label(name, small);
            GUILayout.BeginHorizontal();
            string edited = GUILayout.TextField(field, 10, textField, GUILayout.Width(120));
            if (edited != field) field = new string(edited.Where(char.IsDigit).ToArray());
            int value;
            if (GUILayout.Button("適用", button, GUILayout.Width(50)) && int.TryParse(field, out value)) apply(value);
            if (GUILayout.Button("乱数", button, GUILayout.Width(50)))
            {
                value = UnityEngine.Random.Range(1, 1000000);
                field = value.ToString();
                apply(value);
            }
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------
        // In-game information (human knowledge only)
        // ------------------------------------------------------------------

        private void DrawInfoPanel()
        {
            var view = game.View;
            if (view == null) return;
            var r = Region(new Rect(8, 52, LeftColumn - 16, 210));
            GUILayout.BeginArea(r, panel);
            GUILayout.Label("戦況（あなたが知り得る情報）", header);
            int enemyAlive = view.Enemy.Count(e => e.Alive);
            GUILayout.Label("敵駒 残り " + enemyAlive + " / 31（撃破 " + (31 - enemyAlive) + "）", label);
            var lost = view.Own.Where(p => !p.Alive).GroupBy(p => p.Type).OrderBy(g => g.Key).Select(g => PieceCatalog.JapaneseName(g.Key) + (g.Count() > 1 ? "×" + g.Count() : ""));
            GUILayout.Label("自軍 残り " + view.Own.Count(p => p.Alive) + " / 31", label);
            GUILayout.Label("失った駒: " + (lost.Any() ? string.Join("、", lost) : "なし"), small);
            GUILayout.Space(4);
            GUILayout.Label("敵駒にマウスを合わせると、その駒について観測した履歴を表示します。", small);
            GUILayout.EndArea();
        }

        private static string ResultForPlayer(CombatRecord c)
        {
            return c.Tie ? "相打ち" : c.PlayerWon ? "自軍勝利" : "自軍敗北";
        }

        private void DrawHistory()
        {
            float w = LeftColumn - 16, h = Mathf.Max(200, vh - 52 - 210 - 18);
            var r = Region(new Rect(8, vh - h - 8, w, h));
            GUILayout.BeginArea(r, panel);
            GUILayout.Label("戦闘履歴（公開情報からの判明のみ）", header);
            historyScroll = GUILayout.BeginScrollView(historyScroll);
            if (game.Combats.Count == 0) GUILayout.Label("まだ戦闘はありません", small);
            foreach (var c in Enumerable.Reverse(game.Combats))
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("<b>TURN " + c.Ply + "</b>　" + (c.PlayerAttacked ? "自軍の攻撃" : "敵軍の攻撃"), small);
                GUILayout.Label("自軍：" + PieceCatalog.JapaneseName(c.OwnType) + "（#" + c.OwnNumber + "）　敵軍：Enemy #" + c.EnemyNumber + "（" + game.KnownFacts.Identity(game.View.Enemy.First(e => e.Number == c.EnemyNumber).Id) + "）", small);
                string res = ResultForPlayer(c);
                string color = c.Tie ? "#ffd966" : c.PlayerWon ? "#88ff99" : "#ff8877";
                GUILayout.Label("結果：<color=" + color + ">" + res + "</color>", small);
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawWorldLabels()
        {
            var cam = game.MainCamera;
            if (cam == null) return;
            if (presentation.GraveyardsVisible && game.Phase != Phase.Setup)
            {
                // Same small headings as 対戦モード over the loss areas (count only).
                for (int i = 0; i < 2; i++)
                {
                    bool enemy = i == 1;
                    var hp = cam.WorldToScreenPoint(GraveyardView.HeadingAnchor(enemy));
                    if (hp.z < 0) continue;
                    int lost = enemy ? game.Graveyard.EnemyViews.Count : game.Graveyard.OwnViews.Count;
                    GUI.Label(new Rect(hp.x / scale - 90, (Screen.height - hp.y) / scale - 14, 180, 28), (enemy ? "敵軍の損失　" : "自軍の損失　") + lost + " / 31", kit.Heading);
                }
            }
            if (game.Settings.ShowEnemyNumbers && game.Phase != Phase.Setup)
            {
                foreach (var v in game.PieceViews)
                {
                    if (!v.gameObject.activeSelf || v.Node < 0) continue;
                    if (v.IsOwn && !showMonitor) continue;   // own numbers matter when reading the monitor
                    // Just outside the piece's base edge so the label never covers the carved name.
                    Vector3 sp = cam.WorldToScreenPoint(v.transform.position + new Vector3(0.24f, 0.02f, v.IsOwn ? -0.47f : 0.47f));
                    if (sp.z < 0) continue;
                    var r = new Rect(sp.x / scale - 14, (Screen.height - sp.y) / scale - 7, 28, 14);
                    var style = new GUIStyle(tagStyle);
                    style.normal.textColor = v.IsOwn ? new Color(0.7f, 0.88f, 1f) : new Color(1f, 0.78f, 0.7f);
                    GUI.Label(r, "#" + v.Number, style);
                }
            }
        }

        private void DrawResult()
        {
            float centre = (LeftColumn + vw - (showMonitor ? MonitorWidth() : 0)) / 2f;
            var r = Region(new Rect(centre - 220, vh / 2 - 70, 440, 140));
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x, r.y + 12, r.width, 36), game.ResultText(), new GUIStyle(title) { alignment = TextAnchor.MiddleCenter });
            LastResultText = "TURN " + game.Ply + "　経過 " + UiKit.FormatClock(game.ElapsedSeconds);
            GUI.Label(new Rect(r.x + 20, r.y + 48, r.width - 40, 22), LastResultText, new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            GUI.Label(new Rect(r.x + 20, r.y + 70, r.width - 40, 22), "撃破した敵駒の正体は終局後も公開されません。", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            if (GUI.Button(new Rect(r.x + r.width / 2 - 70, r.y + 94, 140, 32), "新規対局", button)) { game.NewSetup(false); SyncSeedFields(); }
        }

        // ------------------------------------------------------------------
        // CPU thinking monitor
        // ------------------------------------------------------------------

        private string Format(string text, IReadOnlyList<OwnPieceView> own)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = Regex.Replace(text, @"\{O:(\d+)\}", m =>
            {
                int id = int.Parse(m.Groups[1].Value);
                var p = own != null ? own.FirstOrDefault(o => o.Id == id) : game.Cpu?.Knowledge?.OwnPiece(id);
                if (p == null && game.Cpu?.Knowledge != null) p = game.Cpu.Knowledge.OwnPiece(id);
                if (p == null) return "CPU駒";
                return "CPU駒C#" + p.Number + (spoiler ? "(" + PieceCatalog.JapaneseName(p.Type) + ")" : "");
            });
            text = Regex.Replace(text, @"\{S:(.*?)\}", m => spoiler ? m.Groups[1].Value : "");
            return text;
        }

        private DecisionReport CurrentReport()
        {
            var reports = game.Cpu?.Reports;
            if (reports == null || reports.Count == 0) return null;
            if (reportIndex < 0 || reportIndex >= reports.Count) return reports[reports.Count - 1];
            return reports[reportIndex];
        }

        private void DrawMonitor()
        {
            float w = MonitorWidth();
            var r = Region(new Rect(vw - w, 44, w, vh - 44));
            GUILayout.BeginArea(r, panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("CPU思考モニター（研究用）", header, GUILayout.Width(230));
            monitorTab = GUILayout.Toolbar(monitorTab, new[] { "意思決定", "敵駒の推定" }, button, GUILayout.Width(220));
            GUILayout.FlexibleSpace();
            spoiler = GUILayout.Toggle(spoiler, "CPU駒の正体を表示", toggle);
            GUILayout.EndHorizontal();
            GUILayout.Label("※CPUの内部状態を表示します（評価値などから手の内が推測できます）。CPUは敵駒＝あなたの駒の正体を知りません。", small);

            var cpu = game.Cpu;
            var reports = cpu.Reports;
            GUILayout.BeginHorizontal();
            GUILayout.Label("現在 TURN " + (game.Ply + (game.IsFinished ? 0 : 1)) + "　Player Formation Seed " + game.Settings.PlayerFormationSeed
                + "　CPU Formation Seed " + cpu.FormationSeed + "　CPU Decision Seed " + cpu.DecisionSeed, small);
            GUILayout.EndHorizontal();
            GUILayout.Label("CPU配置思想: <b>" + FormationStyles.JapaneseName(cpu.Style) + "</b>" + (cpu.StyleForced ? "（明示指定）" : "（Formation Seed＋戦い方から自動決定）") + "　" + FormationStyles.Summary(cpu.Style), small);
            if (cpu.Profile.HasValue)
                GUILayout.Label("CPUの強さ: <b>" + CpuProfile.StrengthName(cpu.Profile.Value.Strength) + "</b>　戦い方: <b>" + CpuProfile.TemperamentName(cpu.Profile.Value.Temperament) + "</b>　（前進×" + cpu.Personality.ProgressWeight.ToString("0.00")
                    + " 防衛×" + cpu.Personality.DefenseWeight.ToString("0.00") + " リスク回避×" + cpu.Personality.RiskAversion.ToString("0.00") + " 攻撃機会×" + cpu.Personality.OpportunityWeight.ToString("0.00")
                    + "　2手読み " + (cpu.Personality.LookaheadEnabled ? "あり" : "なし") + "　僅差幅 " + cpu.Personality.NearMargin.ToString("0.00") + "）", small);

            GUILayout.BeginHorizontal();
            GUI.enabled = reports.Count > 0;
            int shown = reportIndex < 0 ? reports.Count - 1 : reportIndex;
            if (GUILayout.Button("◀", button, GUILayout.Width(34))) reportIndex = Mathf.Max(0, shown - 1);
            if (GUILayout.Button("▶", button, GUILayout.Width(34))) reportIndex = shown + 1 >= reports.Count ? -1 : shown + 1;
            if (GUILayout.Button("最新", button, GUILayout.Width(50))) reportIndex = -1;
            GUI.enabled = true;
            var report = CurrentReport();
            GUILayout.Label(report == null ? "まだCPUの判断はありません" : "表示中の判断: TURN " + report.Ply + "（" + (shown + 1) + "/" + reports.Count + "）　思考 " + report.ThinkMilliseconds.ToString("0.0") + " ms", small);
            GUILayout.EndHorizontal();

            if (monitorTab == 0) DrawDecision(report, w);
            else DrawBeliefs(report, w);
            GUILayout.EndArea();
        }

        private string PieceLabel(CandidateMove c)
        {
            return "C#" + c.PieceNumber + (spoiler ? PieceCatalog.JapaneseName(c.PieceType) : "");
        }

        private void DrawDecision(DecisionReport report, float w)
        {
            if (report == null) { GUILayout.Label("CPUの手番になると、候補手・評価・採用理由がここに表示されます。", label); return; }
            var c = report.Chosen;
            GUILayout.Space(4);
            GUILayout.Label("<b>最終採用手</b>: " + PieceLabel(c) + "  " + BoardGraph.Describe(c.From) + " → " + BoardGraph.Describe(c.Command.To)
                + (c.TargetEnemyId >= 0 ? "（Enemy #" + c.TargetEnemyNumber + " を攻撃）" : "") + "　評価 " + c.Score.ToString("+0.00;-0.00"), label);
            reasonScroll = GUILayout.BeginScrollView(reasonScroll, GUILayout.Height(150));
            GUILayout.Label("<b>採用理由</b>\n" + Format(report.Reason, report.Own) + "\n<b>状況認識</b>\n" + string.Join("\n", report.Considerations), small);
            GUILayout.EndScrollView();

            GUILayout.Label("<b>検討した候補手</b>（" + report.Candidates.Count + " 手、評価順。各項目は現局面からの変化量、先読み＝相手の最善応手を読んだ補正）", small);
            float[] cols = { 30, 62, 104, 54, 46, 46, 50, 46, 46, 40, 50 };
            string[] heads = { "順位", "駒", "手", "評価", "駒得", "前進", "防衛", "リスク", "機会", "情報", "先読み" };
            GUILayout.BeginHorizontal();
            for (int i = 0; i < heads.Length; i++) GUILayout.Label(heads[i], i >= 3 ? cellRight : cell, GUILayout.Width(cols[i]));
            GUILayout.Label("  備考", cell);
            GUILayout.EndHorizontal();
            candScroll = GUILayout.BeginScrollView(candScroll);
            int rank = 0;
            foreach (var cand in report.Candidates.Take(40))
            {
                rank++;
                GUILayout.BeginHorizontal(cand == c ? chosenRow : GUIStyle.none);
                var t = cand.Terms;
                string[] vals =
                {
                    rank.ToString(), PieceLabel(cand), BoardGraph.Describe(cand.From) + "→" + BoardGraph.Describe(cand.Command.To) + (cand.TargetEnemyId >= 0 ? "×" : ""),
                    cand.Score.ToString("+0.00;-0.00"), F(t.Material), F(t.Progress), F(t.Defense), F(t.Safety), F(t.Opportunity), F(t.Information), F(t.Lookahead),
                };
                for (int i = 0; i < vals.Length; i++) GUILayout.Label(vals[i], i >= 3 ? cellRight : cell, GUILayout.Width(cols[i]));
                GUILayout.Label(cand.TargetEnemyId >= 0 ? "  勝" + P(cand.WinP) + " 分" + P(cand.TieP) + " 負" + P(cand.LoseP) : "", cell);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private static string F(double v) { return Math.Abs(v) < 0.005 ? "·" : v.ToString("+0.00;-0.00"); }
        private static string P(double p) { return Mathf.RoundToInt((float)p * 100) + "%"; }

        private sealed class BeliefRow
        {
            public int Id, Number, Node;
            public bool Alive;
            public double[] Probability;
            public bool[] Allowed;
            public List<BeliefNote> Notes;
        }

        private List<BeliefRow> BeliefRows(DecisionReport report, bool live)
        {
            if (live && game.Cpu.Knowledge != null)
                return game.Cpu.Knowledge.Enemies.Select(b => new BeliefRow { Id = b.Id, Number = b.Number, Node = b.Node, Alive = b.Alive, Probability = b.Probability, Allowed = b.Allowed, Notes = b.Notes }).ToList();
            if (report == null) return new List<BeliefRow>();
            return report.Beliefs.Select(b => new BeliefRow { Id = b.Id, Number = b.Number, Node = b.Node, Alive = b.Alive, Probability = b.Probability, Allowed = b.Allowed, Notes = b.Notes }).ToList();
        }

        private void DrawBeliefs(DecisionReport report, float w)
        {
            // 1.1.1 bug: with a past-turn index and no report (new setup) this dereferenced null every frame.
            bool live = reportIndex < 0 || report == null;
            var rows = BeliefRows(report, live);
            GUILayout.Label(live ? "最新の観測まで反映した推定（あなたの駒＝CPUから見た敵駒）" : "TURN " + report.Ply + " の判断時点の推定", small);
            showOwnTruth = GUILayout.Toggle(showOwnTruth, "実際の駒種を併記（あなた自身の駒なので表示可）", toggle);
            if (rows.Count == 0) return;
            if (selectedEnemy < 0 || rows.All(x => x.Id != selectedEnemy)) selectedEnemy = rows[0].Id;

            // Grid of pieces.
            beliefScroll = GUILayout.BeginScrollView(beliefScroll, GUILayout.Height(Mathf.Min(300, vh * 0.34f)));
            int perRow = 3;
            for (int i = 0; i < rows.Count; i += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Math.Min(rows.Count, i + perRow); j++)
                {
                    var b = rows[j];
                    var top = Enumerable.Range(0, 16).OrderByDescending(t => b.Probability[t]).First();
                    string truth = showOwnTruth && game.View != null ? TruthOf(b.Id) : "";
                    string text = "#" + b.Number + (b.Alive ? " " + BoardGraph.Describe(b.Node) : " 除去") + truth + "\n" + PieceCatalog.JapaneseName((PieceType)top) + " " + P(b.Probability[top]);
                    var st = new GUIStyle(button) { alignment = TextAnchor.MiddleLeft, fontSize = 12, wordWrap = false };
                    if (b.Id == selectedEnemy) st.normal = st.active;
                    if (GUILayout.Button(text, st, GUILayout.Width((w - 48) / perRow), GUILayout.Height(36))) selectedEnemy = b.Id;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            var sel = rows.First(x => x.Id == selectedEnemy);
            GUILayout.Label("<b>Enemy #" + sel.Number + "</b>（あなたの駒 #" + sel.Number + TruthOf(sel.Id) + "）の推定", label);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(250));
            foreach (var t in PieceCatalog.AllTypes)
            {
                double p = sel.Probability[(int)t];
                GUILayout.BeginHorizontal();
                GUILayout.Label(PieceCatalog.JapaneseName(t), cell, GUILayout.Width(48));
                var barRect = GUILayoutUtility.GetRect(110, 14, GUILayout.Width(110));
                GUI.Box(barRect, GUIContent.none, barBack);
                GUI.DrawTexture(new Rect(barRect.x, barRect.y + 2, barRect.width * (float)p, barRect.height - 4), barTex);
                GUILayout.Label(sel.Allowed[(int)t] ? P(p) : "0%  除外", cellRight, GUILayout.Width(66));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            GUILayout.Label("<b>推定の根拠と変化</b>", small);
            noteScroll = GUILayout.BeginScrollView(noteScroll);
            foreach (var n in Enumerable.Reverse(sel.Notes))
            {
                string kind = n.Kind == NoteKind.Excluded ? "除外" : n.Kind == NoteKind.Combat ? "戦闘" : n.Kind == NoteKind.Behaviour ? "挙動" : n.Kind == NoteKind.Rebalance ? "再配分" : n.Kind == NoteKind.Removed ? "除去" : "事前";
                GUILayout.Label("Turn " + n.Ply + " [" + kind + "] " + Format(n.Text, report?.Own) + (string.IsNullOrEmpty(n.After) ? "" : "\n　⇒ " + n.After), small);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        /// <summary>The human's own piece kind (for comparison in the monitor). Comes from the human's own view.</summary>
        private string TruthOf(int id)
        {
            if (!showOwnTruth || game.View == null) return "";
            var p = game.View.OwnById(id);
            return p == null ? "" : " 実:" + PieceCatalog.JapaneseName(p.Type);
        }
    }
}
