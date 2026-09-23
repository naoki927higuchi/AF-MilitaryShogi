using System.Collections.Generic;
using System.Linq;
using MilitaryShogi.Engine;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// 「あそびかた」: a one-screen quick reference (no long scrolling). Top: end conditions. Middle:
    /// all 16 pieces (the game's own face textures). Bottom: the selected piece – large image,
    /// combat chart with piece images, rank ladder for officers, and a movement diagram drawn on the
    /// real board shape. Combat data comes from <see cref="RuleReference"/> (= CombatTable/FlagRule),
    /// movement from <see cref="RuleReference.Example"/> (= MoveRules on BoardGraph), the draw limits
    /// from <see cref="MatchConfig"/>. While open, the game is paused (see Presentation.OpenHelp).
    /// </summary>
    public sealed class HelpUi : MonoBehaviour
    {
        private GameController game;
        private Presentation presentation;
        private UiKit ui;
        private float scale, vw, vh;
        public PieceType Selected = PieceType.General;

        public void Bind(GameController controller, Presentation presentation)
        {
            game = controller;
            this.presentation = presentation;
        }

        private void Update()
        {
            if (presentation != null && presentation.HelpOpen && Input.GetKeyDown(KeyCode.Escape)) presentation.CloseHelp();
        }

        public int LastDrawFrame { get; private set; } = -1;

        private void OnGUI()
        {
            if (game == null || !presentation.HelpOpen) return;
            // On any drawing error the help closes itself, so the game can never stay paused behind it.
            UiGuard.Run("HelpUi", DrawGui, () => presentation.CloseHelp());
        }

        private void DrawGui()
        {
            if (ui == null) ui = new UiKit();
            if (Event.current.type == EventType.Repaint) LastDrawFrame = Time.frameCount;
            GUI.depth = -100;
            scale = UiKit.Scale;
            vw = Screen.width / scale;
            vh = Screen.height / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            game.UiRects.Add(new Rect(0, 0, Screen.width, Screen.height));   // block board clicks
            if (UiKit.Mobile) { DrawMobile(); return; }

            ui.Fill(new Rect(0, 0, vw, vh), new Color(0, 0, 0, 0.55f));
            float w = Mathf.Min(1240, vw - 40), h = Mathf.Min(820, vh - 30);
            var r = new Rect((vw - w) / 2, (vh - h) / 2, w, h);
            ui.Fill(r, new Color(0.07f, 0.05f, 0.04f, 1f));
            ui.Fill(new Rect(r.x, r.y, r.width, 2), new Color(0.75f, 0.55f, 0.28f, 0.8f));
            GUI.Label(new Rect(r.x + 18, r.y + 10, 300, 32), "あそびかた", ui.Big);
            GUI.Label(new Rect(r.x + 180, r.y + 18, 500, 24), "対局は一時停止中です（閉じると再開）", ui.Small);
            UiKit.Spot("help.close", new Rect(r.xMax - 128, r.y + 10, 110, 34));
            if (GUI.Button(new Rect(r.xMax - 128, r.y + 10, 110, 34), "閉じる", ui.Button)) presentation.CloseHelp();

            float y = r.y + 52;
            DrawEndRules(new Rect(r.x + 16, y, w - 32, 90));
            y += 100;
            DrawPieceRow(new Rect(r.x + 16, y, w - 32, 96));
            y += 106;
            DrawDetail(new Rect(r.x + 16, y, w - 32, r.yMax - y - 12));
        }

        // ------------------------------------------------------------------
        // Android: the same content as one scrollable column inside the safe area.
        // ------------------------------------------------------------------

        private readonly TouchScroll mobileScroll = new TouchScroll();
        private float mobileContentHeight = 1400f;

        private void DrawMobile()
        {
            ui.Fill(new Rect(0, 0, vw, vh), new Color(0.05f, 0.04f, 0.03f, 0.98f));
            var sa = Screen.safeArea;
            var safe = new Rect(sa.x / scale, (vh * scale - sa.yMax) / scale, sa.width / scale, sa.height / scale);
            float w = safe.width - 16;
            GUI.Label(new Rect(safe.x + 10, safe.y + 10, 200, 32), "あそびかた", ui.Big);
            var close = new Rect(safe.xMax - 118, safe.y + 6, 110, 48);
            UiKit.Spot("help.close", close);
            if (GUI.Button(close, "閉じる", ui.Button)) { presentation.CloseHelp(); return; }
            GUI.Label(new Rect(safe.x + 10, safe.y + 40, w - 120, 20), "対局は一時停止中です", ui.Small);
            var view = new Rect(safe.x + 8, safe.y + 62, w, safe.height - 66);
            mobileScroll.Begin(view, mobileContentHeight);
            float y = 0;
            // End rules, stacked.
            y = DrawEndRulesStacked(new Rect(0, y, w, 0));
            y += 10;
            // 16 pieces in two rows.
            int n = PieceCatalog.AllTypes.Length, perRow = 8;
            float cell = w / perRow, ch = cell * 1.12f;
            for (int i = 0; i < n; i++)
            {
                var t = PieceCatalog.AllTypes[i];
                var c = new Rect((i % perRow) * cell, y + (i / perRow) * (ch + 4), cell - 4, ch);
                if (t == Selected) ui.Fill(new Rect(c.x - 2, c.y - 2, c.width + 4, c.height + 4), new Color(0.95f, 0.7f, 0.25f, 0.8f));
                if (GUI.Button(c, GUIContent.none, GUIStyle.none) && !mobileScroll.Dragging) Selected = t;
                DrawPiece(new Rect(c.x + 2, c.y + 2, c.width - 4, c.height - 4), t);
            }
            y += 2 * (ch + 4) + 10;
            // Selected piece: image, name, headquarters note.
            DrawPiece(new Rect(0, y, 96, 108), Selected);
            GUI.Label(new Rect(108, y + 20, w - 108, 30), PieceCatalog.JapaneseName(Selected) + "（" + PieceCatalog.Count(Selected) + "枚）", new GUIStyle(ui.Big) { fontSize = 22 });
            GUI.Label(new Rect(108, y + 56, w - 108, 40), PieceCatalog.CanCaptureHeadquarters(Selected) ? "敵総司令部を占領できる" : "総司令部の占領はできない", ui.Small);
            y += 118;
            y = DrawCombat(new Rect(0, y, w, 400)) + 10;
            y = DrawMovement(new Rect(0, y, w, 420)) + 20;
            if (Event.current.type == EventType.Repaint) mobileContentHeight = y;
            mobileScroll.End();
        }

        private float DrawEndRulesStacked(Rect r)
        {
            var cfg = new MatchConfig();
            string[] titles = { "勝利", "敗北", "引き分け（本作の対局終了ルール）" };
            string[] lines = EndRuleLines(cfg);
            Color[] colors = { new Color(0.3f, 0.55f, 0.3f, 0.35f), new Color(0.6f, 0.25f, 0.2f, 0.35f), new Color(0.5f, 0.45f, 0.3f, 0.3f) };
            float y = r.y;
            for (int i = 0; i < 3; i++)
            {
                float h = 30 + ui.Small.CalcHeight(new GUIContent(lines[i]), r.width - 20) + 8;
                var c = new Rect(r.x, y, r.width, h);
                ui.Fill(c, colors[i]);
                GUI.Label(new Rect(c.x + 10, c.y + 4, c.width - 20, 22), titles[i], ui.Header);
                GUI.Label(new Rect(c.x + 10, c.y + 28, c.width - 20, h - 30), lines[i], ui.Small);
                y += h + 6;
            }
            return y;
        }

        private static string[] EndRuleLines(MatchConfig cfg)
        {
            return new[]
            {
                "・大将〜少佐で敵の総司令部を占領\n・敵に動かせる駒がなくなる",
                "・敵に総司令部を占領される\n・自軍に動かせる駒がなくなる\n・投了する（大将〜少佐を全て失ったとき審判が確認）",
                "・双方とも大将〜少佐がいなくなった\n・総" + cfg.MaxPlies + "手に達した\n・" + cfg.MaxPliesWithoutCombat + "手連続で戦闘がない",
            };
        }

        private void DrawEndRules(Rect r)
        {
            var cfg = new MatchConfig();
            float cw = (r.width - 16) / 3f;
            string[] titles = { "勝利", "敗北", "引き分け（本作の対局終了ルール）" };
            string[] lines = EndRuleLines(cfg);
            Color[] colors = { new Color(0.3f, 0.55f, 0.3f, 0.35f), new Color(0.6f, 0.25f, 0.2f, 0.35f), new Color(0.5f, 0.45f, 0.3f, 0.3f) };
            for (int i = 0; i < 3; i++)
            {
                var c = new Rect(r.x + i * (cw + 8), r.y, cw, r.height);
                ui.Fill(c, colors[i]);
                GUI.Label(new Rect(c.x + 10, c.y + 4, c.width - 20, 22), titles[i], ui.Header);
                GUI.Label(new Rect(c.x + 10, c.y + 26, c.width - 20, r.height - 28), lines[i], ui.Small);
            }
        }

        private void DrawPieceRow(Rect r)
        {
            int n = PieceCatalog.AllTypes.Length;
            float cell = Mathf.Min(r.width / n, 76f);
            float x0 = r.x + (r.width - cell * n) / 2f;
            for (int i = 0; i < n; i++)
            {
                var t = PieceCatalog.AllTypes[i];
                var c = new Rect(x0 + i * cell, r.y, cell - 4, r.height);
                if (t == Selected) ui.Fill(new Rect(c.x - 2, c.y - 2, c.width + 4, c.height + 4), new Color(0.95f, 0.7f, 0.25f, 0.8f));
                if (GUI.Button(c, GUIContent.none, GUIStyle.none)) Selected = t;
                DrawPiece(new Rect(c.x + 2, c.y + 2, c.width - 4, c.height - 4), t);
            }
        }

        private void DrawPiece(Rect r, PieceType t)
        {
            var tex = GameAssets.Face(t);
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true);
        }

        private void DrawDetail(Rect r)
        {
            // Left: large image and name.
            var imgRect = new Rect(r.x, r.y, 200, 224);
            ui.Fill(new Rect(imgRect.x, imgRect.y, imgRect.width, r.height), new Color(1, 1, 1, 0.04f));
            DrawPiece(new Rect(imgRect.x + 20, imgRect.y + 10, 160, 180), Selected);
            GUI.Label(new Rect(imgRect.x, imgRect.y + 196, imgRect.width, 30), PieceCatalog.JapaneseName(Selected) + "（" + PieceCatalog.Count(Selected) + "枚）", new GUIStyle(ui.Big) { alignment = TextAnchor.MiddleCenter, fontSize = 22 });
            string note = PieceCatalog.CanCaptureHeadquarters(Selected) ? "敵総司令部を占領できる" : "総司令部の占領はできない";
            GUI.Label(new Rect(imgRect.x + 10, imgRect.y + 232, imgRect.width - 20, 40), note, new GUIStyle(ui.Small) { alignment = TextAnchor.UpperCenter });

            // Middle: combat.
            float mx = r.x + 216, mw = r.width - 216 - 360;
            DrawCombat(new Rect(mx, r.y, mw, r.height));

            // Right: movement.
            DrawMovement(new Rect(r.xMax - 348, r.y, 348, r.height));
        }

        private float DrawCombat(Rect r)
        {
            GUI.Label(new Rect(r.x, r.y, r.width, 24), "戦闘相性（攻めても守っても同じ）", ui.Header);
            float y = r.y + 30;
            if (RuleReference.IsRank(Selected))
            {
                // Rank ladder with the selected piece highlighted.
                float bw = Mathf.Min(58, (r.width - 8 * 14) / 9f);
                float x = r.x;
                for (int i = 0; i < RuleReference.RankOrder.Length; i++)
                {
                    var t = RuleReference.RankOrder[i];
                    var b = new Rect(x, y, bw, 28);
                    ui.Fill(b, t == Selected ? new Color(0.95f, 0.7f, 0.25f, 0.95f) : new Color(1, 1, 1, 0.1f));
                    GUI.Label(b, PieceCatalog.JapaneseName(t), new GUIStyle(ui.Small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = t == Selected ? Color.black : ui.Small.normal.textColor } });
                    x += bw;
                    if (i < 8) { GUI.Label(new Rect(x, y, 14, 28), "＞", new GUIStyle(ui.Small) { alignment = TextAnchor.MiddleCenter }); x += 14; }
                }
                y += 38;
            }
            if (Selected == PieceType.Flag)
            {
                GUI.Label(new Rect(r.x, y, r.width, 120),
                    "軍旗は自分の強さを持たず、戦闘の時点で <b>同じ列のすぐ後ろ（自陣の奥側）にいる味方の駒と同じ強さ</b> で戦う。\n" +
                    "後ろが空いている、または敵の駒なら、どの駒にも負ける。\n軍旗が負けても後ろの駒は取り除かれない。動けない。", ui.Label);
                return y + ui.Label.CalcHeight(new GUIContent("軍旗は自分の強さを持たず、戦闘の時点で同じ列のすぐ後ろ（自陣の奥側）にいる味方の駒と同じ強さで戦う。\n後ろが空いている、または敵の駒なら、どの駒にも負ける。\n軍旗が負けても後ろの駒は取り除かれない。動けない。"), r.width);
            }
            y = Row(r, y, "勝てる", RuleReference.Opponents(Selected, Relation.Wins), new Color(0.45f, 0.9f, 0.5f));
            y = Row(r, y, "相打ち（両方取り除く）", RuleReference.Opponents(Selected, Relation.Ties), new Color(1f, 0.85f, 0.35f));
            y = Row(r, y, "負ける", RuleReference.Opponents(Selected, Relation.Loses), new Color(1f, 0.5f, 0.45f));
            y = Row(r, y, "後ろの駒しだい", RuleReference.Opponents(Selected, Relation.DependsOnBacker), new Color(0.75f, 0.75f, 0.75f));
            if (Selected == PieceType.Mine)
            {
                GUI.Label(new Rect(r.x, y, r.width, 22), "地雷は動けないので、地雷どうしが戦うことはない。", ui.Small);
                y += 24;
            }
            return y;
        }

        private float Row(Rect r, float y, string title, List<PieceType> list, Color color)
        {
            if (list.Count == 0) return y;
            var style = new GUIStyle(ui.Small) { fontStyle = FontStyle.Bold, normal = { textColor = color } };
            GUI.Label(new Rect(r.x, y, r.width, 20), title + "（" + list.Count + "種）", style);
            y += 20;
            float pw = 40, ph = 45, x = r.x;
            foreach (var t in list)
            {
                if (x + pw > r.xMax) { x = r.x; y += ph + 2; }
                DrawPiece(new Rect(x, y, pw, ph), t);
                x += pw + 3;
            }
            return y + ph + 8;
        }

        private float DrawMovement(Rect r)
        {
            var cls = PieceCatalog.MoveClassOf(Selected);
            GUI.Label(new Rect(r.x, r.y, r.width, 24), "動き方", ui.Header);
            GUI.Label(new Rect(r.x, r.y + 26, r.width, Mathf.Max(40f, ui.Small.CalcHeight(new GUIContent(RuleReference.MoveSummary(Selected)), r.width))), RuleReference.MoveSummary(Selected), ui.Small);
            var ex = RuleReference.Example(cls);
            float cell = UiKit.Mobile ? Mathf.Min(40f, r.width / 8.5f) : 26f, band = UiKit.Mobile ? cell * 0.85f : 22f;
            float summary = Mathf.Max(40f, ui.Small.CalcHeight(new GUIContent(RuleReference.MoveSummary(Selected)), r.width));
            float bx = r.x + (r.width - cell * 8) / 2f, by = r.y + 30 + summary;
            DrawMiniBoard(ex, bx, by, cell, band);
            float ly = by + cell * 8 + band + 10;
            foreach (var line in ex.Lines)
            {
                float lh = Mathf.Max(22f, ui.Small.CalcHeight(new GUIContent("・" + line), r.width));
                GUI.Label(new Rect(r.x, ly, r.width, lh), "・" + line, ui.Small);
                ly += lh;
            }
            const string legend = "<size=11>図：金＝この駒　緑●＝移動できる　赤×＝攻撃できる　白＝味方　黒＝敵　◎＝中央丸（手前が自陣）</size>";
            float legendHeight = Mathf.Max(40f, ui.Small.CalcHeight(new GUIContent(legend), r.width));
            GUI.Label(new Rect(r.x, ly + 2, r.width, legendHeight), legend, ui.Small);
            return ly + 2 + legendHeight;
        }

        private static Vector2 CellCenter(int node, float bx, float by, float cell, float band)
        {
            if (BoardGraph.IsCrossing(node))
                return new Vector2(bx + (BoardGraph.X(node) + 0.5f) * cell, by + 4 * cell + band / 2f);
            int x = BoardGraph.X(node), y = BoardGraph.Y(node);
            float py = by + (7 - y) * cell + (y <= 3 ? band : 0) + cell / 2f;
            return new Vector2(bx + x * cell + cell / 2f, py);
        }

        private void DrawMiniBoard(MovementExample ex, float bx, float by, float cell, float band)
        {
            var wood = new Color(0.36f, 0.22f, 0.13f, 1f);
            ui.Fill(new Rect(bx - 6, by - 6, cell * 8 + 12, cell * 8 + band + 12), new Color(0.2f, 0.12f, 0.07f, 1f));
            for (int n = 0; n < BoardGraph.CellCount; n++)
            {
                var c = CellCenter(n, bx, by, cell, band);
                ui.Fill(new Rect(c.x - cell / 2 + 1, c.y - cell / 2 + 1, cell - 2, cell - 2), wood);
            }
            // X gateways: arm ends to centre circle, drawn from the real graph.
            foreach (int crossing in new[] { BoardGraph.LeftCrossing, BoardGraph.RightCrossing })
            {
                var cc = CellCenter(crossing, bx, by, cell, band);
                foreach (int arm in BoardGraph.Neighbors(crossing))
                    ui.Line(cc, CellCenter(arm, bx, by, cell, band), 2f, new Color(0.9f, 0.75f, 0.5f, 0.6f));
                GUI.Label(new Rect(cc.x - 10, cc.y - 11, 20, 22), "◎", new GUIStyle(ui.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 14 });
            }
            // Example pieces.
            for (int n = 0; n < BoardGraph.NodeCount; n++)
            {
                if (ex.Owners[n] == MoveRules.Empty || n == ex.From) continue;
                var c = CellCenter(n, bx, by, cell, band);
                ui.Fill(new Rect(c.x - cell * 0.35f, c.y - cell * 0.35f, cell * 0.7f, cell * 0.7f), ex.Owners[n] == (sbyte)Side.South ? new Color(0.93f, 0.85f, 0.7f) : new Color(0.08f, 0.06f, 0.05f));
            }
            // Paths of multi-square moves (thin), then destinations.
            foreach (var t in ex.Targets)
            {
                Vector2 prev = CellCenter(ex.From, bx, by, cell, band);
                foreach (int p in t.Path)
                {
                    var c = CellCenter(p, bx, by, cell, band);
                    ui.Line(prev, c, 1.5f, new Color(0.5f, 0.95f, 0.55f, 0.35f));
                    prev = c;
                }
            }
            var from = CellCenter(ex.From, bx, by, cell, band);
            ui.Fill(new Rect(from.x - cell * 0.4f, from.y - cell * 0.4f, cell * 0.8f, cell * 0.8f), new Color(0.95f, 0.72f, 0.25f));
            foreach (var t in ex.Targets)
            {
                var c = CellCenter(t.To, bx, by, cell, band);
                var st = new GUIStyle(ui.Small) { alignment = TextAnchor.MiddleCenter, fontSize = t.IsAttack ? 18 : 14, fontStyle = FontStyle.Bold, normal = { textColor = t.IsAttack ? new Color(1f, 0.35f, 0.3f) : new Color(0.5f, 1f, 0.55f) } };
                GUI.Label(new Rect(c.x - 12, c.y - 12, 24, 24), t.IsAttack ? "×" : "●", st);
            }
            if (ex.Targets.Count == 0)
                GUI.Label(new Rect(from.x - 60, from.y + 14, 120, 20), "動けない", new GUIStyle(ui.Small) { alignment = TextAnchor.MiddleCenter });
        }
    }
}
