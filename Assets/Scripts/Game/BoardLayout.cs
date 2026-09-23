using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>Maps board graph nodes to world positions. South (the human) is at -Z, nearest the camera.</summary>
    public static class BoardLayout
    {
        public const float Cell = 1f;
        public const float Band = 1.9f;          // depth of the no-man's-land between camps
        public const float Margin = 0.6f;
        public const float BoardThickness = 0.42f;

        public static float Width { get { return BoardGraph.Columns * Cell + 2 * Margin; } }
        public static float Depth { get { return BoardGraph.Rows * Cell + Band + 2 * Margin; } }

        public static float XOf(int column) { return (column - 3.5f) * Cell; }

        public static float ZOfRow(int y)
        {
            if (y < BoardGraph.CampRows) return -Band / 2f - (BoardGraph.CampRows - 1 - y + 0.5f) * Cell;
            return Band / 2f + (y - BoardGraph.CampRows + 0.5f) * Cell;
        }

        public static Vector3 Node(int node)
        {
            if (BoardGraph.IsCrossing(node)) return new Vector3(XOf(BoardGraph.X(node)), 0f, 0f);
            return new Vector3(XOf(BoardGraph.X(node)), 0f, ZOfRow(BoardGraph.Y(node)));
        }

        /// <summary>Nearest node to a point on the board plane, or -1 if outside every node.</summary>
        public static int Pick(Vector3 p)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int n = 0; n < BoardGraph.NodeCount; n++)
            {
                Vector3 c = Node(n);
                float dx = Mathf.Abs(p.x - c.x), dz = Mathf.Abs(p.z - c.z);
                bool inside = BoardGraph.IsCrossing(n) ? dx * dx + dz * dz < 0.5f * 0.5f : dx < 0.5f * Cell && dz < 0.5f * Cell;
                if (!inside) continue;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = n; }
            }
            return best;
        }
    }
}
