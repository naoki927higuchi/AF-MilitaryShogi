using System;
using System.Collections.Generic;

namespace MilitaryShogi.Rules
{
    /// <summary>配置思想 – setup philosophy.</summary>
    public enum FormationStyle
    {
        Defensive = 0,   // いのちだいじに
        Aggressive = 1,  // ガンガン行こうぜ！
        Balanced = 2,    // じっくりいこうぜ
        Trap = 3,        // わなにはめろ
        Mobile = 4,      // きどうせんだ！
    }

    public static class FormationStyles
    {
        public const int Count = 5;

        public static string JapaneseName(FormationStyle s)
        {
            switch (s)
            {
                case FormationStyle.Defensive: return "いのちだいじに";
                case FormationStyle.Aggressive: return "ガンガン行こうぜ！";
                case FormationStyle.Balanced: return "じっくりいこうぜ";
                case FormationStyle.Trap: return "わなにはめろ";
                default: return "きどうせんだ！";
            }
        }

        public static string Summary(FormationStyle s)
        {
            switch (s)
            {
                case FormationStyle.Defensive: return "防御・重要駒温存重視。将官を後方、地雷で司令部を固め、下級駒を前線の盾にする。";
                case FormationStyle.Aggressive: return "前線圧力・攻撃重視。将官とタンクを突入口付近の前列に置く。";
                case FormationStyle.Balanced: return "バランス型。各駒を中程度の深さに分散させる。";
                case FormationStyle.Trap: return "欺瞞重視。突入口の奥に地雷、前列の軍旗の直後に大将、近くにスパイ。";
                default: return "初動・機動性重視。飛行機・タンク・工兵・騎兵を前列と突入口に集める。";
            }
        }

        /// <summary>Normal play: the style is derived from the formation seed.</summary>
        public static FormationStyle FromSeed(int formationSeed)
        {
            return (FormationStyle)DeterministicRandom.Derive(formationSeed, 0x5717E).Next(Count);
        }
    }

    /// <summary>
    /// Style-driven, seed-perturbed initial placement. Each style gives every piece
    /// kind a preferred depth (0 = back row, 3 = front row), an affinity for the
    /// gateways and for headquarters. Pieces are placed most-constrained first by
    /// maximising desirability + Gaussian noise from the seed. Same (side, style,
    /// seed) always yields the same formation.
    /// </summary>
    public static class FormationGenerator
    {
        private struct Pref
        {
            public float Depth, Gate, Hq;
            public Pref(float depth, float gate, float hq) { Depth = depth; Gate = gate; Hq = hq; }
        }

        private sealed class StyleSpec
        {
            public Pref[] Prefs = new Pref[PieceCatalog.KindCount];
            public PieceType? FlagBacker;   // piece placed directly behind the flag
            public float FlagDepth;
            public float Noise = 0.55f;
        }

        // Placement order: most constrained / most important first.
        private static readonly PieceType[] order =
        {
            PieceType.Flag, PieceType.Mine, PieceType.Mine, PieceType.Mine,
            PieceType.General, PieceType.LieutenantGeneral, PieceType.Spy,
            PieceType.MajorGeneral, PieceType.MajorGeneral, PieceType.Airplane, PieceType.Airplane,
            PieceType.Tank, PieceType.Tank, PieceType.Tank, PieceType.Engineer, PieceType.Engineer, PieceType.Engineer,
            PieceType.Colonel, PieceType.Colonel, PieceType.LieutenantColonel, PieceType.LieutenantColonel,
            PieceType.Major, PieceType.Major, PieceType.Cavalry, PieceType.Cavalry,
            PieceType.Captain, PieceType.Captain, PieceType.FirstLieutenant, PieceType.FirstLieutenant,
            PieceType.SecondLieutenant, PieceType.SecondLieutenant,
        };

        private static StyleSpec Spec(FormationStyle style)
        {
            var s = new StyleSpec();
            Action<PieceType, float, float, float> p = (t, d, g, h) => s.Prefs[(int)t] = new Pref(d, g, h);
            switch (style)
            {
                case FormationStyle.Defensive:
                    p(PieceType.General, 0.8f, 0f, 0.6f); p(PieceType.LieutenantGeneral, 1f, 0.2f, 0.3f); p(PieceType.MajorGeneral, 1.3f, 0.3f, 0f);
                    p(PieceType.Colonel, 1.6f, 0.3f, 0f); p(PieceType.LieutenantColonel, 1.8f, 0.3f, 0f); p(PieceType.Major, 2f, 0.3f, 0f);
                    p(PieceType.Captain, 2.7f, 0.5f, 0f); p(PieceType.FirstLieutenant, 2.8f, 0.5f, 0f); p(PieceType.SecondLieutenant, 3f, 0.6f, 0f);
                    p(PieceType.Airplane, 1.2f, 0f, 0f); p(PieceType.Tank, 2.2f, 0.4f, 0f); p(PieceType.Cavalry, 3f, 0.3f, 0f);
                    p(PieceType.Engineer, 2.2f, 0.2f, 0f); p(PieceType.Spy, 1f, 0f, 0.2f); p(PieceType.Mine, 0.4f, -0.2f, 1.2f);
                    s.FlagDepth = 1f; s.FlagBacker = PieceType.LieutenantGeneral;
                    break;
                case FormationStyle.Aggressive:
                    p(PieceType.General, 2.8f, 1f, 0f); p(PieceType.LieutenantGeneral, 2.8f, 1f, 0f); p(PieceType.MajorGeneral, 2.5f, 0.8f, 0f);
                    p(PieceType.Colonel, 2.3f, 0.6f, 0f); p(PieceType.LieutenantColonel, 2f, 0.4f, 0f); p(PieceType.Major, 2f, 0.3f, 0f);
                    p(PieceType.Captain, 1.3f, 0f, 0f); p(PieceType.FirstLieutenant, 1.2f, 0f, 0f); p(PieceType.SecondLieutenant, 1f, 0f, 0f);
                    p(PieceType.Airplane, 2.5f, 0.2f, 0f); p(PieceType.Tank, 2.9f, 1f, 0f); p(PieceType.Cavalry, 2.2f, 0.4f, 0f);
                    p(PieceType.Engineer, 2f, 0.4f, 0f); p(PieceType.Spy, 2.4f, 0.6f, 0f); p(PieceType.Mine, 0.3f, -0.5f, 1.3f);
                    s.FlagDepth = 1f; s.FlagBacker = PieceType.Major;
                    break;
                case FormationStyle.Balanced:
                    p(PieceType.General, 1.6f, 0.4f, 0f); p(PieceType.LieutenantGeneral, 1.8f, 0.4f, 0f); p(PieceType.MajorGeneral, 2f, 0.4f, 0f);
                    p(PieceType.Colonel, 2f, 0.3f, 0f); p(PieceType.LieutenantColonel, 1.8f, 0.3f, 0f); p(PieceType.Major, 1.8f, 0.3f, 0f);
                    p(PieceType.Captain, 2f, 0.2f, 0f); p(PieceType.FirstLieutenant, 2f, 0.2f, 0f); p(PieceType.SecondLieutenant, 2.2f, 0.2f, 0f);
                    p(PieceType.Airplane, 1.8f, 0f, 0f); p(PieceType.Tank, 2.5f, 0.5f, 0f); p(PieceType.Cavalry, 2.5f, 0.3f, 0f);
                    p(PieceType.Engineer, 2f, 0.3f, 0f); p(PieceType.Spy, 1.5f, 0.2f, 0f); p(PieceType.Mine, 0.8f, 0f, 0.8f);
                    s.FlagDepth = 1f; s.FlagBacker = PieceType.Colonel;
                    break;
                case FormationStyle.Trap:
                    p(PieceType.General, 3f, 0f, 0f); p(PieceType.LieutenantGeneral, 1.5f, 0.3f, 0f); p(PieceType.MajorGeneral, 1.8f, 0.4f, 0f);
                    p(PieceType.Colonel, 1.5f, 0.2f, 0f); p(PieceType.LieutenantColonel, 1.5f, 0.2f, 0f); p(PieceType.Major, 1.6f, 0.2f, 0f);
                    p(PieceType.Captain, 2.6f, 0.6f, 0f); p(PieceType.FirstLieutenant, 2.6f, 0.6f, 0f); p(PieceType.SecondLieutenant, 2.8f, 0.8f, 0f);
                    p(PieceType.Airplane, 1f, 0f, 0f); p(PieceType.Tank, 2f, 0.4f, 0f); p(PieceType.Cavalry, 2.5f, 0.4f, 0f);
                    p(PieceType.Engineer, 0.8f, 0f, 0f); p(PieceType.Spy, 2.8f, 0.2f, 0f); p(PieceType.Mine, 2f, 1.4f, 0f);
                    s.FlagDepth = 3f; s.FlagBacker = PieceType.General;   // a front "flag" that fights as the general
                    break;
                default: // Mobile
                    p(PieceType.General, 1.4f, 0.2f, 0f); p(PieceType.LieutenantGeneral, 1.6f, 0.3f, 0f); p(PieceType.MajorGeneral, 1.8f, 0.3f, 0f);
                    p(PieceType.Colonel, 1.6f, 0.2f, 0f); p(PieceType.LieutenantColonel, 1.5f, 0.2f, 0f); p(PieceType.Major, 1.4f, 0.2f, 0f);
                    p(PieceType.Captain, 1.3f, 0f, 0f); p(PieceType.FirstLieutenant, 1.2f, 0f, 0f); p(PieceType.SecondLieutenant, 1.2f, 0f, 0f);
                    p(PieceType.Airplane, 3f, 0f, 0f); p(PieceType.Tank, 3f, 1.2f, 0f); p(PieceType.Cavalry, 2.8f, 0.8f, 0f);
                    p(PieceType.Engineer, 3f, 1.2f, 0f); p(PieceType.Spy, 1.5f, 0f, 0f); p(PieceType.Mine, 0.3f, -0.3f, 1.2f);
                    s.FlagDepth = 1f; s.FlagBacker = PieceType.Mine;       // flag behaves as a mine
                    break;
            }
            s.Prefs[(int)PieceType.Flag] = new Pref(s.FlagDepth, 0f, 0f);
            return s;
        }

        public static Formation Generate(Side side, FormationStyle style, int seed)
        {
            var spec = Spec(style);
            var rng = DeterministicRandom.Derive(seed, (int)style, (int)side, 0xF0);
            var placed = new Dictionary<int, PieceType>();
            var remaining = new List<PieceType>(order);

            while (remaining.Count > 0)
            {
                PieceType type = remaining[0];
                remaining.RemoveAt(0);
                if (type == PieceType.Flag)
                {
                    PlaceFlag(side, spec, rng, placed, remaining);
                    continue;
                }
                int best = -1;
                double bestScore = double.NegativeInfinity;
                foreach (int node in BoardGraph.CampCells(side))
                {
                    if (placed.ContainsKey(node) || !PlacementRules.IsAllowed(type, node, side)) continue;
                    double score = Desirability(spec.Prefs[(int)type], node, side) + spec.Noise * rng.NextGaussian();
                    if (score > bestScore) { bestScore = score; best = node; }
                }
                // Keep one cell for each piece still to come; the last remaining cell stays empty.
                placed[best] = type;
            }
            var f = new Formation(side, placed);
            PlacementRules.Validate(f);
            return f;
        }

        private static void PlaceFlag(Side side, StyleSpec spec, DeterministicRandom rng, Dictionary<int, PieceType> placed, List<PieceType> remaining)
        {
            int best = -1;
            double bestScore = double.NegativeInfinity;
            foreach (int node in BoardGraph.CampCells(side))
            {
                if (!PlacementRules.IsAllowed(PieceType.Flag, node, side)) continue;
                int back = FlagRule.BackingCell(node, side);
                if (back < 0) continue;
                if (spec.FlagBacker.HasValue && !PlacementRules.IsAllowed(spec.FlagBacker.Value, back, side)) continue;
                double score = Desirability(spec.Prefs[(int)PieceType.Flag], node, side) + spec.Noise * rng.NextGaussian();
                // Flags are easier to protect away from the gateway lanes.
                if (BoardGraph.X(node) == 0 || BoardGraph.X(node) == 7) score -= 0.3;
                if (score > bestScore) { bestScore = score; best = node; }
            }
            placed[best] = PieceType.Flag;
            if (spec.FlagBacker.HasValue)
            {
                placed[FlagRule.BackingCell(best, side)] = spec.FlagBacker.Value;
                remaining.Remove(spec.FlagBacker.Value);
            }
        }

        private static double Desirability(Pref pref, int node, Side side)
        {
            int depth = BoardGraph.Depth(node, side);
            double score = -Math.Abs(depth - pref.Depth);
            int gateDistance = int.MaxValue;
            foreach (int n in BoardGraph.CampCells(side))
                if (BoardGraph.IsArmEnd(n)) gateDistance = Math.Min(gateDistance, BoardGraph.Distance(node, n));
            double gateProximity = Math.Max(0.0, 1.0 - gateDistance / 4.0);
            score += 0.9 * pref.Gate * gateProximity;
            if (BoardGraph.IsHeadquarters(node, side)) score += 1.5 * pref.Hq;
            return score;
        }
    }
}
