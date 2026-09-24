using System;
using System.Collections.Generic;
using System.Linq;
using MilitaryShogi.Rules;

namespace MilitaryShogi.Observation
{
    /// <summary>
    /// 棋譜再現 (1.5.0): the view a player had after TURN t of a finished game, rebuilt from that
    /// player's final view only – the initial nodes of every piece plus the public move history
    /// (moves, combat pairs and outcomes). Nothing else is stored, so the replay can never disagree
    /// with the game. TURN 0 is the initial placement; TURN n is the position after n plies (a ply
    /// with a combat is move + combat + removals). The view of TURN t contains the first t history
    /// entries only, so anything derived from it (PlayerKnownFacts, loss areas, tooltips) knows
    /// exactly what the player knew then – never something learned later.
    /// </summary>
    public static class GameReplay
    {
        /// <summary>Number of plies (the last TURN).</summary>
        public static int Length(PlayerView final) { return final.History.Count; }

        public static PlayerView ViewAt(PlayerView final, int turn)
        {
            int n = final.History.Count;
            if (turn < 0 || turn > n) throw new ArgumentOutOfRangeException(nameof(turn));
            if (turn == n) return final;

            var node = new Dictionary<int, int>();
            foreach (var p in final.Own) node[p.Id] = p.InitialNode;
            foreach (var e in final.Enemy) node[e.Id] = e.InitialNode;
            for (int i = 0; i < turn; i++) Apply(final.History[i], node);

            var own = final.Own.Select(p => new OwnPieceView(p.Id, p.Number, p.Type, node[p.Id], p.InitialNode)).ToArray();
            var enemy = final.Enemy.Select(e => new EnemyPieceView(e.Id, e.Number, node[e.Id], e.InitialNode)).ToArray();
            // The board before the next ply is recorded with that ply.
            var owners = (sbyte[])final.History[turn].OwnersBefore.Clone();
            return new PlayerView(final.Me, turn, final.History[turn].Mover, GameStatus.Playing, null, EndReason.None,
                own, enemy, final.History.Take(turn).ToArray(), owners);
        }

        private static void Apply(ObservedMove m, Dictionary<int, int> node)
        {
            var c = m.Combat;
            if (c == null) { node[m.PieceId] = m.To; return; }
            switch (c.Outcome)
            {
                case CombatOutcome.AttackerWins:
                    node[c.DefenderId] = -1;
                    node[c.AttackerId] = m.To;
                    break;
                case CombatOutcome.DefenderWins:
                    node[c.AttackerId] = -1;
                    break;
                default:
                    node[c.AttackerId] = -1;
                    node[c.DefenderId] = -1;
                    break;
            }
        }
    }
}
