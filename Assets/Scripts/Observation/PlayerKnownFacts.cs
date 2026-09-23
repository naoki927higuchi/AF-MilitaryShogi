using System;
using System.Collections.Generic;
using System.Linq;
using MilitaryShogi.Rules;

namespace MilitaryShogi.Observation
{
    /// <summary>Human knowledge: logical possibilities only, never CPU beliefs or hidden state.
    /// Replays public history, including removals. A singleton is a proven kind, not a guess.</summary>
    public sealed class PlayerKnownFacts
    {
        private readonly Side me;
        private readonly Dictionary<int, HashSet<PieceType>> candidates = new Dictionary<int, HashSet<PieceType>>();
        private readonly Dictionary<int, PieceType> own = new Dictionary<int, PieceType>();
        private readonly int[] at = Enumerable.Repeat(-1, BoardGraph.NodeCount).ToArray();
        private int processed;

        public PlayerKnownFacts(PlayerView view)
        {
            me = view.Me;
            foreach (var p in view.Own) { own[p.Id] = p.Type; at[p.InitialNode] = p.Id; }
            foreach (var p in view.Enemy)
            {
                candidates[p.Id] = new HashSet<PieceType>(PieceCatalog.AllTypes.Where(t => PlacementRules.IsAllowed(t, p.InitialNode, me.Opponent())));
                at[p.InitialNode] = p.Id;
            }
            Update(view);
        }

        public PieceType? KnownType(int id)
        {
            HashSet<PieceType> set;
            return candidates.TryGetValue(id, out set) && set.Count == 1 ? set.First() : (PieceType?)null;
        }

        public PieceType[] Candidates(int id)
        {
            HashSet<PieceType> set;
            return candidates.TryGetValue(id, out set) ? set.OrderBy(t => t).ToArray() : new PieceType[0];
        }

        public string Identity(int id)
        {
            var kind = KnownType(id);
            return kind.HasValue ? "判明：" + PieceCatalog.JapaneseName(kind.Value) : "正体不明";
        }

        public void Update(PlayerView view)
        {
            if (view.Me != me || view.History.Count < processed) throw new ArgumentException("Different observation stream");
            while (processed < view.History.Count)
            {
                var m = view.History[processed++];
                if (m.Mover != me)
                    candidates[m.PieceId].RemoveWhere(t => !MoveRules.Generate(t, m.Mover, m.From, m.OwnersBefore)
                        .Any(move => move.To == m.To && move.Jumped == m.Jumped && move.Path.SequenceEqual(m.Path)));
                var c = m.Combat;
                if (c != null)
                {
                    if (m.Mover != me)
                    {
                        var defender = OwnStrength(c.DefenderId, m);
                        candidates[c.AttackerId].RemoveWhere(t => CombatTable.Resolve(t, defender) != c.Outcome);
                    }
                    else
                    {
                        var attacker = own[c.AttackerId];
                        var strengths = EnemyFlagStrengths(m).ToArray();
                        candidates[c.DefenderId].RemoveWhere(t => t == PieceType.Flag
                            ? !strengths.Any(s => CombatTable.Resolve(attacker, s) == c.Outcome)
                            : CombatTable.Resolve(attacker, t) != c.Outcome);
                    }
                }
                // No removed candidate set is discarded: facts survive death.
                at[m.From] = -1;
                if (c == null || c.Outcome == CombatOutcome.AttackerWins) at[m.To] = m.PieceId;
                else if (c.Outcome == CombatOutcome.Tie) at[m.To] = -1;
            }
        }

        private PieceType? OwnStrength(int id, ObservedMove m)
        {
            if (own[id] != PieceType.Flag) return own[id];
            int back = FlagRule.BackingCell(m.To, me);
            PieceType type;
            return back >= 0 && m.OwnersBefore[back] == (sbyte)me && own.TryGetValue(at[back], out type) ? type : (PieceType?)null;
        }

        private IEnumerable<PieceType?> EnemyFlagStrengths(ObservedMove m)
        {
            int back = FlagRule.BackingCell(m.To, me.Opponent());
            HashSet<PieceType> set;
            if (back >= 0 && m.OwnersBefore[back] == (sbyte)me.Opponent() && candidates.TryGetValue(at[back], out set))
            {
                foreach (var t in set) if (t != PieceType.Flag) yield return t;
            }
            else yield return null;
        }
    }
}
