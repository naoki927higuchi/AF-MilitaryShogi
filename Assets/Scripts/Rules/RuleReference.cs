using System.Collections.Generic;
using System.Linq;

namespace MilitaryShogi.Rules
{
    public enum Relation { Wins, Ties, Loses, DependsOnBacker }

    /// <summary>A small board example for the help screen, computed with the real movement rules.</summary>
    public sealed class MovementExample
    {
        public MoveClass Class;
        public int From;
        public sbyte[] Owners;                 // own (South) / enemy (North) example pieces
        public List<MoveTarget> Targets;       // exactly MoveRules.Generate(...) for this setup
        public string[] Lines;                 // short explanation
    }

    /// <summary>
    /// Data for 「あそびかた」 derived from CombatTable / FlagRule / MoveRules / BoardGraph, so the
    /// help cannot disagree with the game: nothing here is written by hand except wording.
    /// </summary>
    public static class RuleReference
    {
        public static readonly PieceType[] RankOrder =
        {
            PieceType.General, PieceType.LieutenantGeneral, PieceType.MajorGeneral, PieceType.Colonel,
            PieceType.LieutenantColonel, PieceType.Major, PieceType.Captain, PieceType.FirstLieutenant, PieceType.SecondLieutenant,
        };

        public static bool IsRank(PieceType t) { return t <= PieceType.SecondLieutenant; }

        /// <summary>
        /// Result for <paramref name="piece"/> meeting <paramref name="other"/> (either attacking or
        /// defending – the table is symmetric). Flags fight as the piece behind them.
        /// </summary>
        public static Relation Against(PieceType piece, PieceType other)
        {
            if (piece == PieceType.Flag || other == PieceType.Flag) return Relation.DependsOnBacker;
            if (piece == PieceType.Mine && other == PieceType.Mine) return Relation.Ties;   // cannot happen (neither moves)
            int r = CombatTable.Compare(piece, other);
            return r > 0 ? Relation.Wins : r < 0 ? Relation.Loses : Relation.Ties;
        }

        public static List<PieceType> Opponents(PieceType piece, Relation relation)
        {
            return PieceCatalog.AllTypes.Where(o => Against(piece, o) == relation).ToList();
        }

        public static string MoveSummary(PieceType t)
        {
            switch (PieceCatalog.MoveClassOf(t))
            {
                case MoveClass.Step: return "前後左右に1マス";
                case MoveClass.Charger: return "前後左右に1マス、または前へ2マス（途中の駒は飛び越せない）";
                case MoveClass.Slider: return "縦横に何マスでも（途中の駒は飛び越せない）";
                case MoveClass.Flyer: return "縦に何マスでも（駒を飛び越せる・突入口を通らず敵陣へ）、横に1マス";
                default: return "動けない";
            }
        }

        private static sbyte[] Empty()
        {
            var o = new sbyte[BoardGraph.NodeCount];
            for (int i = 0; i < o.Length; i++) o[i] = MoveRules.Empty;
            return o;
        }

        /// <summary>One example per movement class, drawn on the real board (South = you).</summary>
        public static MovementExample Example(MoveClass cls)
        {
            var o = Empty();
            int from;
            string[] lines;
            switch (cls)
            {
                case MoveClass.Step:
                    from = BoardGraph.Cell(2, 3);
                    lines = new[] { "前後左右に1マス。", "突入口の端からは中央丸へ進める。", "中央丸から敵陣へ入るのも1マス。" };
                    break;
                case MoveClass.Charger:
                    from = BoardGraph.Cell(2, 2);
                    lines = new[] { "前後左右に1マス、または前へ2マス。", "途中に駒があると2マスは進めない。", "図のように2マス目が中央丸ならそこで止まる。" };
                    break;
                case MoveClass.Slider:
                    from = BoardGraph.Cell(0, 1);
                    o[BoardGraph.Cell(4, 1)] = (sbyte)Side.South;
                    lines = new[] { "縦横に何マスでも進める。", "駒は飛び越せない（手前で止まる）。", "中央丸に入るとそこで止まる。" };
                    break;
                case MoveClass.Flyer:
                    from = BoardGraph.Cell(4, 1);
                    o[BoardGraph.Cell(4, 2)] = (sbyte)Side.South;
                    o[BoardGraph.Cell(4, 3)] = (sbyte)Side.South;
                    o[BoardGraph.Cell(4, 5)] = (sbyte)Side.North;
                    lines = new[] { "縦に何マスでも。途中の駒を飛び越せる。", "突入口を通らず、同じ列で敵陣へ入れる。", "横へは1マス。中央丸には止まらない。" };
                    break;
                default:
                    from = BoardGraph.Cell(3, 1);
                    lines = new[] { "地雷・軍旗は動かせない。", "地雷・軍旗は突入口に置けない。", "軍旗は最後列に置けない。" };
                    break;
            }
            o[from] = (sbyte)Side.South;
            var targets = new List<MoveTarget>();
            MoveRules.Generate(cls, Side.South, from, o, targets);
            return new MovementExample { Class = cls, From = from, Owners = o, Targets = targets, Lines = lines };
        }
    }
}
