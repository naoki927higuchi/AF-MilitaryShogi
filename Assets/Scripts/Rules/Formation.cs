using System;
using System.Collections.Generic;
using System.Linq;

namespace MilitaryShogi.Rules
{
    /// <summary>Initial placement of one army: node -> piece kind (31 of the 32 camp cells).</summary>
    public sealed class Formation
    {
        public readonly Side Side;
        private readonly Dictionary<int, PieceType> pieces;

        public Formation(Side side, IDictionary<int, PieceType> pieces)
        {
            Side = side;
            this.pieces = new Dictionary<int, PieceType>(pieces);
        }

        public IReadOnlyDictionary<int, PieceType> Pieces { get { return pieces; } }
        public int Count { get { return pieces.Count; } }
        public bool TryGet(int node, out PieceType type) { return pieces.TryGetValue(node, out type); }

        public Formation Clone() { return new Formation(Side, pieces); }

        /// <summary>The one camp cell left empty.</summary>
        public int EmptyCell()
        {
            foreach (int n in BoardGraph.CampCells(Side)) if (!pieces.ContainsKey(n)) return n;
            return -1;
        }

        /// <summary>
        /// Move or swap for manual editing. Returns false (and changes nothing) if the
        /// result would break a placement rule.
        /// </summary>
        public bool TryMoveOrSwap(int from, int to)
        {
            if (from == to || !pieces.ContainsKey(from)) return false;
            if (!BoardGraph.InCamp(to, Side)) return false;
            PieceType moving = pieces[from];
            PieceType other;
            bool swap = pieces.TryGetValue(to, out other);
            if (!PlacementRules.IsAllowed(moving, to, Side)) return false;
            if (swap && !PlacementRules.IsAllowed(other, from, Side)) return false;
            pieces.Remove(from);
            if (swap) { pieces.Remove(to); pieces[from] = other; }
            pieces[to] = moving;
            return true;
        }

        public string Signature()
        {
            return string.Join(",", pieces.OrderBy(p => p.Key).Select(p => p.Key + ":" + (int)p.Value));
        }
    }

    /// <summary>
    /// Placement restrictions (fixed rule set):
    ///  - pieces start inside their own 8x4 camp, one piece per cell (one cell stays empty);
    ///  - mines and the flag may not stand on a gateway arm end (突入口);
    ///  - the flag may not stand on the back row (so never in headquarters).
    /// Mines are allowed in headquarters.
    /// </summary>
    public static class PlacementRules
    {
        public static bool IsAllowed(PieceType type, int node, Side side)
        {
            if (!BoardGraph.InCamp(node, side)) return false;
            if ((type == PieceType.Mine || type == PieceType.Flag) && BoardGraph.IsArmEnd(node)) return false;
            if (type == PieceType.Flag && BoardGraph.Depth(node, side) == 0) return false;
            return true;
        }

        public static void Validate(Formation f)
        {
            PieceCatalog.ValidateArmy(f.Pieces.Values);
            foreach (var p in f.Pieces)
                if (!IsAllowed(p.Value, p.Key, f.Side))
                    throw new ArgumentException(PieceCatalog.JapaneseName(p.Value) + " cannot start at " + BoardGraph.Describe(p.Key));
        }

        public static bool IsValid(Formation f)
        {
            try { Validate(f); return true; } catch (ArgumentException) { return false; }
        }
    }
}
