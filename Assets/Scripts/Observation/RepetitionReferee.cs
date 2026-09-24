using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MilitaryShogi.Rules;

namespace MilitaryShogi.Observation
{
    /// <summary>
    /// The referee's intervention in a stalemate of repeated positions (1.4.0).
    ///
    /// Some positions make shuffling back and forth the rational best play for both sides (e.g. a
    /// defender must mirror an attacker in front of its headquarters, and the attacker – possibly a
    /// bluff – need not do anything else). The game would then never progress. When the same PUBLIC
    /// position with the same side to move has occurred <see cref="Occurrences"/> times, the referee
    /// intervenes once for that repetition, toward the side for which breaking the repetition is the
    /// risk: the side whose headquarters is closer to the opponent's pieces that move in the cycle.
    ///
    /// Only public information is used: which piece id stands on which node, who is to move, and the
    /// public move history. Piece kinds (own or enemy), whether a piece can capture a headquarters,
    /// beliefs, evaluations and the true outcome are never read – this class sits in the Observation
    /// layer and cannot reach them. So the fact that the referee did or did not intervene reveals
    /// nothing about hidden kinds.
    ///
    /// This is a rule of the game, not a change to the CPU's thinking. After an intervention toward
    /// the CPU, <see cref="ContinuesRepetition"/> tells which moves would re-enter the intervened cycle;
    /// the game then plays the CPU's best-scored move that does not (the CPU's evaluation is untouched).
    /// </summary>
    public sealed class RepetitionReferee
    {
        /// <summary>A position reached for the 5th time (the exchange repeated four times after it first appeared).</summary>
        public const int Occurrences = 5;

        private readonly List<string> keys = new List<string>();                  // position after each ply (index 0 = start)
        private readonly List<ObservedMove> plies = new List<ObservedMove>();     // plies[i] led to keys[i + 1]
        private readonly Dictionary<string, int> count = new Dictionary<string, int>();
        private readonly List<HashSet<string>> handled = new List<HashSet<string>>();
        private readonly Dictionary<Side, HashSet<string>> restricted = new Dictionary<Side, HashSet<string>>
        {
            { Side.South, new HashSet<string>() }, { Side.North, new HashSet<string>() },
        };
        public readonly List<RefereeIntervention> Interventions = new List<RefereeIntervention>();

        /// <summary>Public position key: side to move, then every live piece as id@node (both sides, sorted by id).</summary>
        public static string Key(Side toMove, IEnumerable<KeyValuePair<int, int>> pieceNodes)
        {
            var sb = new StringBuilder(toMove == Side.South ? "S" : "N");
            foreach (var p in pieceNodes.Where(p => p.Value >= 0).OrderBy(p => p.Key)) sb.Append('|').Append(p.Key).Append('@').Append(p.Value);
            return sb.ToString();
        }

        /// <summary>Piece id → node of every live piece in a view. Reads ids and nodes only.</summary>
        public static Dictionary<int, int> PublicNodes(PlayerView view)
        {
            var map = new Dictionary<int, int>();
            foreach (var p in view.Own) if (p.Node >= 0) map[p.Id] = p.Node;
            foreach (var e in view.Enemy) if (e.Node >= 0) map[e.Id] = e.Node;
            return map;
        }

        public static string Key(PlayerView view) { return Key(view.ToMove, PublicNodes(view)); }

        /// <summary>Start of the game (the position before the first move).</summary>
        public void Start(PlayerView view) { Add(view, null); }

        /// <summary>After each ply. Returns the intervention if one happens now, else null.</summary>
        public RefereeIntervention Record(PlayerView view)
        {
            var move = view.History.Count > 0 ? view.History[view.History.Count - 1] : null;
            string key = Add(view, move);
            if (view.Status != GameStatus.Playing || count[key] < Occurrences) return null;
            if (handled.Any(h => h.Contains(key))) return null;            // one intervention per repetition

            // The cycle: positions since the previous occurrence of this position.
            int now = keys.Count - 1, previous = keys.LastIndexOf(key, now - 1);
            var cycle = new HashSet<string>();
            for (int i = previous; i <= now; i++) cycle.Add(keys[i]);
            var moving = new Dictionary<Side, HashSet<int>> { { Side.South, new HashSet<int>() }, { Side.North, new HashSet<int>() } };
            for (int i = previous; i < now; i++)
            {
                var m = plies[i];
                moving[m.Mover].Add(m.From);
                moving[m.Mover].Add(m.To);
            }
            var targets = Defenders(moving);
            handled.Add(cycle);
            foreach (var t in targets) restricted[t].UnionWith(cycle);
            var intervention = new RefereeIntervention(view.Ply, targets, count[key], cycle.Count, now - previous);
            Interventions.Add(intervention);
            return intervention;
        }

        /// <summary>
        /// The side(s) the referee addresses: the side whose headquarters is nearer (in board steps,
        /// ignoring occupancy) to the nodes the opponent's pieces use in the cycle. Equal → both.
        /// Uses nodes only – never what the pieces are.
        /// </summary>
        public static Side[] Defenders(Dictionary<Side, HashSet<int>> cycleNodesByMover)
        {
            int Threat(Side defender)
            {
                var attackerNodes = cycleNodesByMover[defender.Opponent()];
                return attackerNodes.Count == 0 ? int.MaxValue : attackerNodes.Min(n => BoardGraph.DistanceToHeadquarters(n, defender));
            }
            int south = Threat(Side.South), north = Threat(Side.North);
            if (south == north) return new[] { Side.South, Side.North };
            return new[] { south < north ? Side.South : Side.North };
        }

        /// <summary>True once the referee has intervened toward this side (for some repetition).</summary>
        public bool IsRestricted(Side side) { return restricted[side].Count > 0; }

        /// <summary>
        /// Whether a quiet move of <paramref name="side"/> would re-enter a repetition the referee
        /// intervened in toward that side. Attacks never do: every combat removes a piece.
        /// </summary>
        public bool ContinuesRepetition(Side side, PlayerView view, MoveCommand move, bool isAttack)
        {
            if (isAttack || restricted[side].Count == 0) return false;
            var map = PublicNodes(view);
            if (!map.ContainsKey(move.PieceId)) return false;
            map[move.PieceId] = move.To;
            return restricted[side].Contains(Key(view.ToMove.Opponent(), map));
        }

        /// <summary>How often the current public position has occurred.</summary>
        public int CurrentOccurrences { get { return keys.Count == 0 ? 0 : count[keys[keys.Count - 1]]; } }

        private string Add(PlayerView view, ObservedMove move)
        {
            var map = PublicNodes(view);
            string key = Key(view.ToMove, map);
            keys.Add(key);
            if (move != null) plies.Add(move);
            int c;
            count[key] = count.TryGetValue(key, out c) ? c + 1 : 1;
            return key;
        }
    }

    /// <summary>One intervention: when, toward whom, and the size of the repetition. No hidden information.</summary>
    public sealed class RefereeIntervention
    {
        public readonly int Ply;
        public readonly Side[] Targets;
        public readonly int Occurrences;
        public readonly int CyclePositions;
        public readonly int CyclePlies;

        public RefereeIntervention(int ply, Side[] targets, int occurrences, int cyclePositions, int cyclePlies)
        {
            Ply = ply; Targets = targets; Occurrences = occurrences; CyclePositions = cyclePositions; CyclePlies = cyclePlies;
        }

        public bool Addresses(Side side) { return Array.IndexOf(Targets, side) >= 0; }

        public override string ToString()
        {
            return "ply " + Ply + " → " + string.Join("+", Targets) + " (position seen " + Occurrences + "×, cycle " + CyclePlies + " plies)";
        }
    }
}
