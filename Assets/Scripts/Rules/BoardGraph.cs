using System;
using System.Collections.Generic;

namespace MilitaryShogi.Rules
{
    /// <summary>South moves first and is drawn nearest the camera (the human by default).</summary>
    public enum Side { South = 0, North = 1 }

    public static class SideExtensions
    {
        public static Side Opponent(this Side s) { return s == Side.South ? Side.North : Side.South; }
        /// <summary>+1 when "forward" means increasing y (South), -1 for North.</summary>
        public static int Forward(this Side s) { return s == Side.South ? 1 : -1; }
    }

    public enum NodeKind { Cell, Crossing }

    /// <summary>
    /// The 31-piece board ("8列31枚編成X字型") as a graph.
    ///
    /// Cells: 8 columns (x = 0..7) by 8 rows (y = 0..7). y = 0..3 is the South camp
    /// (y = 0 its back row), y = 4..7 the North camp (y = 7 its back row). Node id of a
    /// cell is y * 8 + x. The camps are NOT adjacent: between y = 3 and y = 4 lies a
    /// band that ordinary pieces can only cross through the two X-shaped gateways.
    ///
    /// Each X gateway has a centre point (中央丸, node 64 = left, 65 = right) linked to
    /// four arm-end cells: the front-row cells in columns {0, 2} (left) or {5, 7}
    /// (right) of both camps. Moving arm end -> centre -> any arm end is one step each.
    /// The centre holds one piece and combat there is allowed. Multi-square moves stop
    /// when they enter a centre and cannot pass through it.
    ///
    /// The airplane ignores the gateways: along a column it treats the two front rows
    /// as adjacent (and jumps anything in between). It never stops on a centre.
    /// Headquarters: the two middle cells of each back row (x = 3, 4).
    /// </summary>
    public static class BoardGraph
    {
        public const int Columns = 8;
        public const int Rows = 8;
        public const int CellCount = 64;
        public const int LeftCrossing = 64;
        public const int RightCrossing = 65;
        public const int NodeCount = 66;
        public const int CampRows = 4;

        private static readonly int[] leftArmColumns = { 0, 2 };
        private static readonly int[] rightArmColumns = { 5, 7 };
        private static readonly int[][] adjacency;
        private static readonly int[,] shortestSteps;

        static BoardGraph()
        {
            adjacency = new int[NodeCount][];
            for (int n = 0; n < NodeCount; n++)
            {
                var list = new List<int>(4);
                if (n < CellCount)
                {
                    foreach (var d in OrthogonalDirections)
                    {
                        int t = Step(n, d.dx, d.dy);
                        if (t >= 0) list.Add(t);
                    }
                }
                else
                {
                    list.AddRange(ArmEnds(n));
                }
                adjacency[n] = list.ToArray();
            }
            shortestSteps = new int[NodeCount, NodeCount];
            for (int s = 0; s < NodeCount; s++)
            {
                for (int t = 0; t < NodeCount; t++) shortestSteps[s, t] = int.MaxValue;
                var q = new Queue<int>();
                shortestSteps[s, s] = 0;
                q.Enqueue(s);
                while (q.Count > 0)
                {
                    int u = q.Dequeue();
                    foreach (int v in adjacency[u])
                        if (shortestSteps[s, v] == int.MaxValue) { shortestSteps[s, v] = shortestSteps[s, u] + 1; q.Enqueue(v); }
                }
            }
        }

        public struct Direction
        {
            public readonly int dx, dy;
            public Direction(int dx, int dy) { this.dx = dx; this.dy = dy; }
        }

        public static readonly Direction[] OrthogonalDirections =
        {
            new Direction(0, 1), new Direction(0, -1), new Direction(1, 0), new Direction(-1, 0),
        };

        public static int X(int node) { return node < CellCount ? node % Columns : (node == LeftCrossing ? 1 : 6); }
        public static int Y(int node) { return node < CellCount ? node / Columns : -1; }
        public static bool IsCell(int node) { return node >= 0 && node < CellCount; }
        public static bool IsCrossing(int node) { return node == LeftCrossing || node == RightCrossing; }
        public static NodeKind KindOf(int node) { return IsCell(node) ? NodeKind.Cell : NodeKind.Crossing; }
        public static int Cell(int x, int y)
        {
            if (x < 0 || x >= Columns || y < 0 || y >= Rows) return -1;
            return y * Columns + x;
        }

        public static IReadOnlyList<int> Neighbors(int node) { return adjacency[node]; }

        /// <summary>Which camp a cell belongs to. Crossings belong to neither.</summary>
        public static bool InCamp(int node, Side side)
        {
            if (!IsCell(node)) return false;
            return side == Side.South ? Y(node) < CampRows : Y(node) >= CampRows;
        }

        /// <summary>Row counted from the side's own back row (0 = back, 3 = front).</summary>
        public static int Depth(int node, Side side)
        {
            if (!IsCell(node)) return -1;
            return side == Side.South ? Y(node) : Rows - 1 - Y(node);
        }

        public static int CampCell(Side side, int x, int depth)
        {
            return Cell(x, side == Side.South ? depth : Rows - 1 - depth);
        }

        public static IEnumerable<int> CampCells(Side side)
        {
            for (int d = 0; d < CampRows; d++)
                for (int x = 0; x < Columns; x++)
                    yield return CampCell(side, x, d);
        }

        public static bool IsHeadquarters(int node, Side owner)
        {
            return IsCell(node) && Depth(node, owner) == 0 && InCamp(node, owner) && (X(node) == 3 || X(node) == 4);
        }

        public static int[] Headquarters(Side owner) { return new[] { CampCell(owner, 3, 0), CampCell(owner, 4, 0) }; }

        /// <summary>Front-row cells touching an X gateway (突入口). Mines and the flag may not start here.</summary>
        public static bool IsArmEnd(int node)
        {
            if (!IsCell(node)) return false;
            int y = Y(node), x = X(node);
            if (y != CampRows - 1 && y != CampRows) return false;
            return x == 0 || x == 2 || x == 5 || x == 7;
        }

        public static int CrossingOfArmEnd(int node)
        {
            if (!IsArmEnd(node)) return -1;
            return X(node) <= 2 ? LeftCrossing : RightCrossing;
        }

        public static IEnumerable<int> ArmEnds(int crossing)
        {
            int[] cols = crossing == LeftCrossing ? leftArmColumns : rightArmColumns;
            foreach (int x in cols) yield return Cell(x, CampRows - 1);
            foreach (int x in cols) yield return Cell(x, CampRows);
        }

        /// <summary>
        /// One straight step used by line moves. Inside a camp it is the neighbouring
        /// cell; across the band, an arm-end cell steps into its crossing; everything
        /// else (including stepping out of a crossing) returns -1.
        /// </summary>
        public static int Step(int node, int dx, int dy)
        {
            if (!IsCell(node)) return -1;
            int x = X(node), y = Y(node);
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || nx >= Columns || ny < 0 || ny >= Rows) return -1;
            bool crossesBand = (y == CampRows - 1 && ny == CampRows) || (y == CampRows && ny == CampRows - 1);
            if (crossesBand) return dx == 0 && IsArmEnd(node) ? CrossingOfArmEnd(node) : -1;
            return Cell(nx, ny);
        }

        /// <summary>Airplane column step: like Step, but the two front rows are directly adjacent.</summary>
        public static int FlyStep(int node, int dy)
        {
            if (!IsCell(node)) return -1;
            int ny = Y(node) + dy;
            if (ny < 0 || ny >= Rows) return -1;
            return Cell(X(node), ny);
        }

        /// <summary>Steps between two nodes for a one-square mover, ignoring occupancy.</summary>
        public static int Distance(int a, int b) { return shortestSteps[a, b]; }

        public static int DistanceToHeadquarters(int node, Side owner)
        {
            int best = int.MaxValue;
            foreach (int h in Headquarters(owner)) best = Math.Min(best, Distance(node, h));
            return best;
        }

        public static string Describe(int node)
        {
            if (node == LeftCrossing) return "左中央丸";
            if (node == RightCrossing) return "右中央丸";
            return (char)('a' + X(node)) + (Y(node) + 1).ToString();
        }
    }
}
