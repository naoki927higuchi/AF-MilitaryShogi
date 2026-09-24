using System;

namespace MilitaryShogi.Rules
{
    public enum CombatOutcome { AttackerWins, DefenderWins, Tie }

    /// <summary>
    /// Result of two pieces meeting. The table does not depend on who attacks
    /// (the spy beats the general in both directions). Source: the 31-piece
    /// 勝敗表 distributed on the 足立区生涯学習センター site (gunzin.pdf); the mine row there is
    /// shifted by one column and is read as "loses to airplane and engineer, ties
    /// with everything else", matching the mine column.
    ///
    /// The flag has no strength of its own: it fights as the friendly piece directly
    /// behind it (see <see cref="FlagRule"/>), or loses to everything if there is none.
    /// </summary>
    public static class CombatTable
    {
        // Row = piece A, column = piece B, value = result for A.
        // Order: 大将 中将 少将 大佐 中佐 少佐 大尉 中尉 少尉 飛行機 タンク 騎兵 工兵 スパイ 地雷
        private static readonly string[] table =
        {
            "=ooooooooooooX=", // 大将
            "X=oooooooooooo=", // 中将
            "XX=ooooooooooo=", // 少将
            "XXX=oooooXXooo=", // 大佐
            "XXXX=ooooXXooo=", // 中佐
            "XXXXX=oooXXooo=", // 少佐
            "XXXXXX=ooXXooo=", // 大尉
            "XXXXXXX=oXXooo=", // 中尉
            "XXXXXXXX=XXooo=", // 少尉
            "XXXoooooo=ooooo", // 飛行機
            "XXXooooooX=oXo=", // タンク
            "XXXXXXXXXXX=oo=", // 騎兵
            "XXXXXXXXXXoX=oo", // 工兵
            "oXXXXXXXXXXXX==", // スパイ
            "=========X==X==", // 地雷 (mine vs mine never happens)
        };

        private static readonly int[,] result; // +1 A wins, -1 A loses, 0 tie

        static CombatTable()
        {
            const int n = 15;
            result = new int[n, n];
            if (table.Length != n) throw new InvalidOperationException("combat table rows");
            for (int a = 0; a < n; a++)
            {
                if (table[a].Length != n) throw new InvalidOperationException("combat table row " + a + " has " + table[a].Length + " columns");
                for (int b = 0; b < n; b++)
                {
                    char c = table[a][b];
                    result[a, b] = c == 'o' ? 1 : c == 'X' ? -1 : 0;
                }
            }
        }

        /// <summary>+1 if <paramref name="a"/> beats <paramref name="b"/>, -1 if it loses, 0 for a tie (both removed).</summary>
        public static int Compare(PieceType a, PieceType b)
        {
            if (a == PieceType.Flag || b == PieceType.Flag)
                throw new ArgumentException("Resolve the flag to its effective strength first (FlagRule).");
            return result[(int)a, (int)b];
        }

        /// <summary>
        /// Resolve a fight between effective strengths. <paramref name="defender"/> may be
        /// null for a flag with no friendly piece behind it, which loses to everything.
        /// </summary>
        public static CombatOutcome Resolve(PieceType attacker, PieceType? defender)
        {
            if (defender == null) return CombatOutcome.AttackerWins;
            int r = Compare(attacker, defender.Value);
            return r > 0 ? CombatOutcome.AttackerWins : r < 0 ? CombatOutcome.DefenderWins : CombatOutcome.Tie;
        }
    }

    /// <summary>
    /// 軍旗: fights with the strength of the friendly piece in the same column one row
    /// closer to its own back row, evaluated at the moment of combat. If that cell is
    /// empty, holds an enemy, or does not exist, the flag loses to every piece.
    /// The backing piece is not affected by the flag's fight.
    /// </summary>
    public static class FlagRule
    {
        public static int BackingCell(int flagNode, Side owner)
        {
            if (!BoardGraph.IsCell(flagNode)) return -1;
            int behind = BoardGraph.Cell(BoardGraph.X(flagNode), BoardGraph.Y(flagNode) - owner.Forward());
            return behind >= 0 && BoardGraph.InCamp(behind, owner) ? behind : -1;
        }
    }
}
