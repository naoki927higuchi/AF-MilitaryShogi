using System.Collections.Generic;

namespace MilitaryShogi.Rules
{
    /// <summary>A destination reachable by one piece in one move.</summary>
    public struct MoveTarget
    {
        public int To;
        /// <summary>Nodes passed in order, ending with <see cref="To"/>. Used for animation and observation.</summary>
        public int[] Path;
        /// <summary>Number of occupied nodes flown over (airplane only).</summary>
        public int Jumped;
        public bool IsAttack;
    }

    /// <summary>
    /// Movement rules. Pure functions of (move class, side, origin, who occupies each node).
    /// Occupancy is only "empty / South / North" – exactly what both players can see –
    /// so the same code serves the referee, the human UI and the CPU's inference.
    /// </summary>
    public static class MoveRules
    {
        public const sbyte Empty = -1;

        public static List<MoveTarget> Generate(PieceType type, Side side, int from, sbyte[] owners)
        {
            var list = new List<MoveTarget>();
            Generate(PieceCatalog.MoveClassOf(type), side, from, owners, list);
            return list;
        }

        public static void Generate(MoveClass cls, Side side, int from, sbyte[] owners, List<MoveTarget> output)
        {
            switch (cls)
            {
                case MoveClass.Immobile: return;
                case MoveClass.Step: Adjacent(side, from, owners, output); return;
                case MoveClass.Charger:
                    Adjacent(side, from, owners, output);
                    ForwardTwo(side, from, owners, output);
                    return;
                case MoveClass.Slider: Slide(side, from, owners, output); return;
                case MoveClass.Flyer: Fly(side, from, owners, output); return;
            }
        }

        public static bool CanReach(MoveClass cls, Side side, int from, int to, sbyte[] owners)
        {
            var list = new List<MoveTarget>(16);
            Generate(cls, side, from, owners, list);
            foreach (var m in list) if (m.To == to) return true;
            return false;
        }

        private static bool IsOwn(sbyte[] owners, int node, Side side) { return owners[node] == (sbyte)side; }
        private static bool IsEnemy(sbyte[] owners, int node, Side side) { return owners[node] != Empty && owners[node] != (sbyte)side; }

        private static void Add(List<MoveTarget> output, sbyte[] owners, Side side, int to, int[] path, int jumped)
        {
            output.Add(new MoveTarget { To = to, Path = path, Jumped = jumped, IsAttack = IsEnemy(owners, to, side) });
        }

        private static void Adjacent(Side side, int from, sbyte[] owners, List<MoveTarget> output)
        {
            foreach (int n in BoardGraph.Neighbors(from))
                if (!IsOwn(owners, n, side)) Add(output, owners, side, n, new[] { n }, 0);
        }

        private static void ForwardTwo(Side side, int from, sbyte[] owners, List<MoveTarget> output)
        {
            int fwd = side.Forward();
            int first = BoardGraph.Step(from, 0, fwd);
            if (first < 0 || BoardGraph.IsCrossing(first) || owners[first] != Empty) return; // stops in a centre / cannot jump
            int second = BoardGraph.Step(first, 0, fwd);
            if (second < 0 || IsOwn(owners, second, side)) return;
            Add(output, owners, side, second, new[] { first, second }, 0);
        }

        private static void Slide(Side side, int from, sbyte[] owners, List<MoveTarget> output)
        {
            if (BoardGraph.IsCrossing(from)) { Adjacent(side, from, owners, output); return; }
            foreach (var d in BoardGraph.OrthogonalDirections)
            {
                var path = new List<int>();
                int n = BoardGraph.Step(from, d.dx, d.dy);
                while (n >= 0)
                {
                    if (IsOwn(owners, n, side)) break;
                    path.Add(n);
                    Add(output, owners, side, n, path.ToArray(), 0);
                    if (owners[n] != Empty || BoardGraph.IsCrossing(n)) break;
                    n = BoardGraph.Step(n, d.dx, d.dy);
                }
            }
        }

        private static void Fly(Side side, int from, sbyte[] owners, List<MoveTarget> output)
        {
            foreach (int dy in new[] { 1, -1 })
            {
                var path = new List<int>();
                int jumped = 0;
                int n = BoardGraph.FlyStep(from, dy);
                while (n >= 0)
                {
                    path.Add(n);
                    if (!IsOwn(owners, n, side)) Add(output, owners, side, n, path.ToArray(), jumped);
                    if (owners[n] != Empty) jumped++;
                    n = BoardGraph.FlyStep(n, dy);
                }
            }
            foreach (int dx in new[] { 1, -1 })
            {
                int n = BoardGraph.Step(from, dx, 0);
                if (n >= 0 && !IsOwn(owners, n, side)) Add(output, owners, side, n, new[] { n }, 0);
            }
        }
    }
}
