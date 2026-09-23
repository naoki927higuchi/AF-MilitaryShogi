using System;
using System.Collections.Generic;

namespace MilitaryShogi.Rules
{
    /// <summary>
    /// The 16 kinds of pieces in the 31-piece set. This enum is public knowledge
    /// (every player knows which kinds exist); what is secret is which enemy piece
    /// has which kind. That mapping lives only in the Engine assembly.
    /// </summary>
    public enum PieceType
    {
        General = 0,            // 大将
        LieutenantGeneral = 1,  // 中将
        MajorGeneral = 2,       // 少将
        Colonel = 3,            // 大佐
        LieutenantColonel = 4,  // 中佐
        Major = 5,              // 少佐
        Captain = 6,            // 大尉
        FirstLieutenant = 7,    // 中尉
        SecondLieutenant = 8,   // 少尉
        Airplane = 9,           // 飛行機
        Tank = 10,              // タンク
        Cavalry = 11,           // 騎兵
        Engineer = 12,          // 工兵
        Spy = 13,               // スパイ
        Mine = 14,              // 地雷
        Flag = 15,              // 軍旗
    }

    /// <summary>How a piece kind moves. Several kinds share one class.</summary>
    public enum MoveClass
    {
        Step,       // one square orthogonally (officers, spy)
        Charger,    // one square orthogonally, or two straight forward (tank, cavalry)
        Slider,     // any distance orthogonally, no jumping (engineer)
        Flyer,      // any distance forward/back with jumping, one square sideways (airplane)
        Immobile,   // mine, flag
    }

    public static class PieceCatalog
    {
        public const int KindCount = 16;
        public const int PiecesPerSide = 31;

        public static readonly PieceType[] AllTypes =
        {
            PieceType.General, PieceType.LieutenantGeneral, PieceType.MajorGeneral,
            PieceType.Colonel, PieceType.LieutenantColonel, PieceType.Major,
            PieceType.Captain, PieceType.FirstLieutenant, PieceType.SecondLieutenant,
            PieceType.Airplane, PieceType.Tank, PieceType.Cavalry, PieceType.Engineer,
            PieceType.Spy, PieceType.Mine, PieceType.Flag,
        };

        private static readonly int[] counts = { 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 3, 1, 3, 1 };

        private static readonly string[] names =
        {
            "大将", "中将", "少将", "大佐", "中佐", "少佐", "大尉", "中尉", "少尉",
            "飛行機", "タンク", "騎兵", "工兵", "スパイ", "地雷", "軍旗",
        };

        /// <summary>Number of pieces of this kind each side starts with.</summary>
        public static int Count(PieceType t) { return counts[(int)t]; }

        public static string JapaneseName(PieceType t) { return names[(int)t]; }

        /// <summary>大将〜少佐: the only kinds that win by occupying the enemy headquarters.</summary>
        public static bool CanCaptureHeadquarters(PieceType t) { return t <= PieceType.Major; }

        public static bool IsMobile(PieceType t) { return t != PieceType.Mine && t != PieceType.Flag; }

        public static MoveClass MoveClassOf(PieceType t)
        {
            switch (t)
            {
                case PieceType.Tank:
                case PieceType.Cavalry: return MoveClass.Charger;
                case PieceType.Engineer: return MoveClass.Slider;
                case PieceType.Airplane: return MoveClass.Flyer;
                case PieceType.Mine:
                case PieceType.Flag: return MoveClass.Immobile;
                default: return MoveClass.Step;
            }
        }

        /// <summary>Stars shown on the face for officers (3/2/1), 0 for special pieces.</summary>
        public static int Stars(PieceType t)
        {
            if (t <= PieceType.MajorGeneral) return 3;
            if (t <= PieceType.Major) return 2;
            if (t <= PieceType.SecondLieutenant) return 1;
            return 0;
        }

        /// <summary>The standard 31-piece army as a flat list (one entry per piece).</summary>
        public static List<PieceType> StandardArmy()
        {
            var list = new List<PieceType>(PiecesPerSide);
            foreach (var t in AllTypes)
                for (int i = 0; i < Count(t); i++) list.Add(t);
            return list;
        }

        public static void ValidateArmy(IEnumerable<PieceType> army)
        {
            var seen = new int[KindCount];
            int total = 0;
            foreach (var t in army) { seen[(int)t]++; total++; }
            if (total != PiecesPerSide) throw new ArgumentException("An army must have 31 pieces, got " + total);
            foreach (var t in AllTypes)
                if (seen[(int)t] != Count(t))
                    throw new ArgumentException(JapaneseName(t) + " must appear " + Count(t) + " times, got " + seen[(int)t]);
        }
    }
}
