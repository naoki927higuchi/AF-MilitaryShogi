using System.Collections.Generic;
using System.Linq;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>Shared public-observation tooltip. All positions are GUI coordinates, not cursor offsets.</summary>
    public sealed class EnemyTooltip
    {
        private readonly GameController game;
        private UiKit ui;
        public Rect LastRect { get; private set; }
        public Rect LastAnchor { get; private set; }
        public string LastText { get; private set; }
        internal int PreviewPieceId = -1; // in-player acceptance screenshots only

        public EnemyTooltip(GameController game) { this.game = game; }

        public static Rect ScreenBounds(Camera cam, Bounds bounds, float scale)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var p = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var s = cam.WorldToScreenPoint(p);
                var q = new Vector2(s.x / scale, (Screen.height - s.y) / scale);
                min = Vector2.Min(min, q); max = Vector2.Max(max, q);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        public static float Clearance(Rect anchor) { return Mathf.Max(96f, Mathf.Max(anchor.width, anchor.height) * 1.5f); }

        private static float Overlap(Rect a, Rect b)
        {
            return Mathf.Max(0, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin)) * Mathf.Max(0, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
        }

        public static Rect Place(Rect anchor, Vector2 size, Rect screen, IEnumerable<Rect> panels)
        {
            var blocked = panels.ToArray();
            float gap = Clearance(anchor);
            var exclusion = Rect.MinMaxRect(anchor.xMin - gap, anchor.yMin - gap, anchor.xMax + gap, anchor.yMax + gap);
            float w = Mathf.Min(size.x, screen.width), h = Mathf.Min(size.y, screen.height);
            float right = exclusion.xMax, left = exclusion.xMin - w;
            float cy = Mathf.Clamp(anchor.center.y - h / 2, screen.yMin, screen.yMax - h);
            var xs = new List<float> { right, left, screen.xMin, screen.xMax - w, anchor.center.x - w / 2 };
            var ys = new List<float> { cy, exclusion.yMin - h, exclusion.yMax, screen.yMin, screen.yMax - h };
            foreach (var p in blocked) { xs.Add(p.xMax + 8); xs.Add(p.xMin - w - 8); ys.Add(p.yMax + 8); ys.Add(p.yMin - h - 8); }
            Rect best = new Rect(screen.position, new Vector2(w, h));
            double score = double.MaxValue;
            foreach (float x in xs) foreach (float y in ys)
            {
                var r = new Rect(Mathf.Clamp(x, screen.xMin, screen.xMax - w), Mathf.Clamp(y, screen.yMin, screen.yMax - h), w, h);
                // Preserve the large exclusion around the entire piece first, avoid UI second.
                // With equal clearance, prefer right, then left, then above/below.
                float preference = r.xMin >= right - 0.1f ? 0 : r.xMax <= exclusion.xMin + 0.1f ? 1 : 2;
                double cost = Overlap(r, exclusion) * 1000000.0 + blocked.Sum(p => Overlap(r, p)) * 1000.0
                    + preference * 100 + Mathf.Abs(r.center.y - anchor.center.y) * 0.05f + Mathf.Abs(r.center.x - anchor.center.x) * 0.001f;
                if (cost < score) { score = cost; best = r; }
            }
            return best;
        }

        /// <param name="revealTruth">研究モード「CPU駒の正体を表示」: show the true kind (never in 対戦).</param>
        public string TextFor(PieceView piece, bool revealTruth = false)
        {
            var truth = revealTruth ? game.Session.ResearchTrueKind(piece.Id) : null;
            string identity = truth.HasValue ? PieceCatalog.JapaneseName(truth.Value) + "（研究：真値表示）" : game.KnownFacts.Identity(piece.Id);
            var lines = new List<string> { "<b>Enemy #" + piece.Number + "</b>　" + identity + "　位置 " + BoardGraph.Describe(piece.Node) };
            int moves = 0;
            foreach (var m in game.View.History)
            {
                if (m.PieceId == piece.Id)
                {
                    moves++;
                    string what = BoardGraph.Describe(m.From) + "→" + BoardGraph.Describe(m.To) + (m.Jumped > 0 ? "（" + m.Jumped + "枚飛び越え）" : m.Path.Length > 1 ? "（" + m.Path.Length + "マス）" : "");
                    lines.Add("TURN " + m.Ply + "  移動 " + what);
                }
                if (m.Combat != null && (m.Combat.AttackerId == piece.Id || m.Combat.DefenderId == piece.Id))
                {
                    var c = game.Combats.FirstOrDefault(rec => rec.Ply == m.Ply);
                    if (c != null) lines.Add("TURN " + m.Ply + "  戦闘: 自軍" + PieceCatalog.JapaneseName(c.OwnType) + "(#" + c.OwnNumber + ")と → " + (c.Tie ? "相打ち" : c.PlayerWon ? "自軍勝利" : "自軍敗北"));
                }
            }
            if (moves == 0) lines.Add("まだ一度も動いていない");
            if (lines.Count > 12) lines = lines.Take(1).Concat(new[] { "…以前の記録は研究モードの戦闘履歴へ" }).Concat(lines.Skip(lines.Count - 10)).ToList();
            return string.Join("\n", lines);
        }

        /// <param name="enabled">対戦: the 敵駒の観測情報 setting. 研究: always true.</param>
        public void Draw(bool modalOpen, bool revealTruth, bool enabled)
        {
            LastText = null;
            if (!enabled || modalOpen || game.View == null || game.KnownFacts == null || game.Phase == Phase.Setup || game.Phase == Phase.Animating || game.Phase == Phase.Finished) return;
            var piece = PreviewPieceId >= 0 ? game.PieceViews.FirstOrDefault(p => p.Id == PreviewPieceId) : game.PieceAtNode(game.HoverNode);
            if (piece == null || piece.IsOwn || !piece.gameObject.activeSelf) return;
            float scale = UiKit.Scale;
            var panels = game.UiRects.Select(r => new Rect(r.x / scale, r.y / scale, r.width / scale, r.height / scale)).ToArray();
            var mouse = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
            if (PreviewPieceId < 0 && panels.Any(r => r.Contains(mouse))) return;
            if (ui == null) ui = new UiKit();
            var screen = new Rect(8, 8, Screen.width / scale - 16, Screen.height / scale - 16);
            float width = Mathf.Min(370, screen.width);
            LastText = TextFor(piece, revealTruth);
            float height = Mathf.Min(screen.height, ui.Small.CalcHeight(new GUIContent(LastText), width - 24) + 20);
            LastAnchor = ScreenBounds(game.MainCamera, piece.WorldBounds, scale);
            LastRect = Place(LastAnchor, new Vector2(width, height), screen, panels);
            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var gold = new Color(0.9f, 0.75f, 0.43f, 0.9f);
            var a = LastAnchor;
            ui.Fill(new Rect(a.xMin - 2, a.yMin - 2, a.width + 4, 1), gold);
            ui.Fill(new Rect(a.xMin - 2, a.yMax + 1, a.width + 4, 1), gold);
            ui.Fill(new Rect(a.xMin - 2, a.yMin - 2, 1, a.height + 4), gold);
            ui.Fill(new Rect(a.xMax + 1, a.yMin - 2, 1, a.height + 4), gold);
            ui.Fill(LastRect, new Color(0.06f, 0.045f, 0.035f, 0.98f));
            GUI.Label(new Rect(LastRect.x + 12, LastRect.y + 10, width - 24, height - 20), LastText, ui.Small);
            GUI.matrix = saved;
        }
    }
}
