using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;

namespace MilitaryShogi.Cpu
{
    public enum NoteKind
    {
        Prior,      // weak prior from the initial position
        Excluded,   // hard evidence removed candidates
        Combat,     // combat outcome constrained candidates
        Behaviour,  // soft evidence from behaviour
        Rebalance,  // probabilities shifted because of other pieces (count constraint)
        Removed,    // piece left the board
    }

    /// <summary>One line of "why the estimate changed" for one enemy piece.</summary>
    public sealed class BeliefNote
    {
        public readonly int Ply;
        public readonly NoteKind Kind;
        public readonly string Text;
        public string After { get; internal set; }   // estimate summary after this ply's update

        public BeliefNote(int ply, NoteKind kind, string text) { Ply = ply; Kind = kind; Text = text; }
        public override string ToString() { return "Turn " + Ply + ": " + Text + (string.IsNullOrEmpty(After) ? "" : "  ⇒ " + After); }
    }

    /// <summary>What the CPU believes about one enemy piece.</summary>
    public sealed class EnemyBelief
    {
        public readonly int Id;
        public readonly int Number;
        public readonly int InitialNode;
        public int Node;
        public bool Alive { get { return Node >= 0; } }
        public int MoveCount;
        public int LastMovedPly = -1;
        public int LastFrom = -1;
        public int CombatCount;
        public readonly bool[] Allowed = new bool[PieceCatalog.KindCount];
        public readonly double[] LogWeight = new double[PieceCatalog.KindCount];
        public readonly double[] Probability = new double[PieceCatalog.KindCount];
        public readonly List<BeliefNote> Notes = new List<BeliefNote>();

        internal EnemyBelief(int id, int number, int initialNode)
        {
            Id = id; Number = number; InitialNode = initialNode; Node = initialNode;
            for (int t = 0; t < PieceCatalog.KindCount; t++) Allowed[t] = true;
        }

        public double P(PieceType t) { return Probability[(int)t]; }

        public IEnumerable<PieceType> Candidates()
        {
            foreach (var t in PieceCatalog.AllTypes) if (Allowed[(int)t]) yield return t;
        }

        public double Entropy()
        {
            double h = 0;
            foreach (double p in Probability) if (p > 1e-9) h -= p * Math.Log(p, 2);
            return h;
        }

        public string TopSummary(int n = 3)
        {
            var parts = PieceCatalog.AllTypes.Where(t => Probability[(int)t] >= 0.005)
                .OrderByDescending(t => Probability[(int)t]).Take(n)
                .Select(t => PieceCatalog.JapaneseName(t) + " " + Math.Round(Probability[(int)t] * 100) + "%");
            return string.Join(" / ", parts);
        }
    }

    /// <summary>
    /// The CPU's knowledge about the opponent. Built ONLY from <see cref="PlayerView"/>
    /// (own kinds, enemy ids/positions, public move/combat history).
    ///
    /// Inference:
    ///  1. Hard constraints per piece (Allowed[]): a kind is removed when it could not
    ///     have produced an observed move (checked with the real movement rules against
    ///     the observed occupancy), when a combat outcome against a known own piece is
    ///     impossible for it, or when the initial position forbids it.
    ///  2. Soft evidence (LogWeight[]): weak priors from typical human placement and
    ///     from behaviour (attacking pieces tend to be strong).
    ///  3. Count constraint: the 31 enemy pieces are exactly the standard army.
    ///     Marginals are obtained by iterative proportional fitting of the
    ///     (piece x kind) weight matrix so that every row sums to 1 and every kind's
    ///     column sums to its count – e.g. once one airplane is identified, the other
    ///     pieces' airplane probability drops automatically.
    /// </summary>
    public sealed class CpuKnowledge
    {
        public readonly Side Me;
        public Side EnemySide { get { return Me.Opponent(); } }
        private readonly Dictionary<int, EnemyBelief> beliefs = new Dictionary<int, EnemyBelief>();
        private readonly Dictionary<int, OwnPieceView> own = new Dictionary<int, OwnPieceView>();
        private readonly int[] pieceAt = new int[BoardGraph.NodeCount];
        private int processed;
        public int LastPly { get; private set; }

        public IEnumerable<EnemyBelief> Enemies { get { return beliefs.Values.OrderBy(b => b.Number); } }
        public EnemyBelief Belief(int id) { EnemyBelief b; return beliefs.TryGetValue(id, out b) ? b : null; }
        public bool IsEnemy(int id) { return beliefs.ContainsKey(id); }
        public int PieceAt(int node) { return pieceAt[node]; }
        public OwnPieceView OwnPiece(int id) { OwnPieceView p; return own.TryGetValue(id, out p) ? p : null; }

        public CpuKnowledge(PlayerView initial)
        {
            Me = initial.Me;
            for (int i = 0; i < pieceAt.Length; i++) pieceAt[i] = -1;
            foreach (var p in initial.Own) { own[p.Id] = p; pieceAt[p.InitialNode] = p.Id; }
            foreach (var e in initial.Enemy)
            {
                var b = new EnemyBelief(e.Id, e.Number, e.InitialNode);
                beliefs[e.Id] = b;
                pieceAt[e.InitialNode] = e.Id;
                ApplyInitialPosition(b);
            }
            Solve(0, new HashSet<int>(beliefs.Keys), null);
            foreach (var b in beliefs.Values)
                foreach (var n in b.Notes) n.After = b.TopSummary();
        }

        // ------------------------------------------------------------------
        // Updates
        // ------------------------------------------------------------------

        public void Update(PlayerView view)
        {
            if (view.Me != Me) throw new ArgumentException("view of another player");
            foreach (var p in view.Own) own[p.Id] = p;
            while (processed < view.History.Count)
            {
                var move = view.History[processed++];
                var before = Snapshot();
                var touched = new HashSet<int>();
                var newNotes = new List<Tuple<EnemyBelief, BeliefNote>>();
                if (move.Mover == EnemySide) ObserveEnemyMove(move, touched, newNotes);
                else ObserveOwnMove(move, touched, newNotes);
                ApplyToBoard(move);
                Solve(move.Ply, touched, before);
                foreach (var t in newNotes) t.Item2.After = t.Item1.TopSummary();
                LastPly = move.Ply;
            }
        }

        private void ObserveEnemyMove(ObservedMove move, HashSet<int> touched, List<Tuple<EnemyBelief, BeliefNote>> notes)
        {
            var b = beliefs[move.PieceId];
            touched.Add(b.Id);
            b.MoveCount++;
            b.LastMovedPly = move.Ply;
            b.LastFrom = move.From;

            // (1) It moved: mines and the flag never move.
            var gone = Exclude(b, t => !PieceCatalog.IsMobile(t));
            if (gone.Count > 0) Note(notes, b, move.Ply, NoteKind.Excluded, "移動を観測 → " + Names(gone) + " を除外");

            // (2) Could each remaining kind have made exactly this move from this position?
            string shape = DescribeShape(move, EnemySide);
            gone = Exclude(b, t => !MoveRules.CanReach(PieceCatalog.MoveClassOf(t), EnemySide, move.From, move.To, move.OwnersBefore));
            if (gone.Count > 0)
            {
                string conclusion = b.Candidates().Count() == 1 ? PieceCatalog.JapaneseName(b.Candidates().First()) + " に確定" : Names(gone) + " を除外";
                Note(notes, b, move.Ply, NoteKind.Excluded, shape + " → 移動範囲から " + conclusion);
            }

            if (move.Combat != null)
            {
                var myPiece = own[move.Combat.DefenderId];
                PieceType? myStrength = OwnStrengthAt(myPiece, move);
                b.CombatCount++;
                var outcome = move.Combat.Outcome;
                gone = Exclude(b, t => !PieceCatalog.IsMobile(t) || CombatTable.Resolve(t, myStrength) != outcome);
                string mine = OwnLabel(myPiece) + (myPiece.Type == PieceType.Flag ? "{S:(強さ:" + (myStrength.HasValue ? PieceCatalog.JapaneseName(myStrength.Value) : "単独") + ")}" : "");
                string res = outcome == CombatOutcome.AttackerWins ? "敵の勝ち" : outcome == CombatOutcome.DefenderWins ? "敵の負け" : "相打ち";
                Note(notes, b, move.Ply, NoteKind.Combat, "自軍 " + mine + " を攻撃して" + res + " → " + (gone.Count > 0 ? Names(gone) + " を除外" : "候補変化なし"));
                // Behaviour: humans usually attack with pieces they trust.
                Soft(b, t => t <= PieceType.Major || t == PieceType.Airplane || t == PieceType.Tank ? 0.15 : -0.10);
                Note(notes, b, move.Ply, NoteKind.Behaviour, "自ら攻撃を仕掛けた → 強い駒の可能性を少し上げる");
                if (outcome != CombatOutcome.AttackerWins) MarkRemovedIf(b, outcome, move, notes);
            }
            else
            {
                // Behaviour: a piece heading straight for our headquarters is more likely an officer that can capture it.
                int dBefore = BoardGraph.DistanceToHeadquarters(move.From, Me);
                int dAfter = BoardGraph.DistanceToHeadquarters(move.To, Me);
                if (dAfter < dBefore && dAfter <= 3)
                {
                    Soft(b, t => PieceCatalog.CanCaptureHeadquarters(t) ? 0.08 : 0.0);
                    Note(notes, b, move.Ply, NoteKind.Behaviour, "自軍司令部へ接近（残り" + dAfter + "） → 佐官以上の可能性を少し上げる");
                }
            }
        }

        private void ObserveOwnMove(ObservedMove move, HashSet<int> touched, List<Tuple<EnemyBelief, BeliefNote>> notes)
        {
            if (move.Combat == null) return;
            var b = beliefs[move.Combat.DefenderId];
            touched.Add(b.Id);
            b.CombatCount++;
            var myPiece = own[move.Combat.AttackerId];
            var outcome = move.Combat.Outcome;

            // The enemy flag fights as the enemy piece directly behind it (if any).
            int back = FlagRule.BackingCell(move.To, EnemySide);
            EnemyBelief backer = null;
            if (back >= 0 && move.OwnersBefore[back] == (sbyte)EnemySide && pieceAt[back] >= 0) backer = Belief(pieceAt[back]);

            var gone = Exclude(b, t =>
            {
                if (t != PieceType.Flag) return CombatTable.Resolve(myPiece.Type, t) != outcome;
                if (backer == null) return outcome != CombatOutcome.AttackerWins;
                foreach (var bt in backer.Candidates())
                    if (bt != PieceType.Flag && CombatTable.Resolve(myPiece.Type, bt) == outcome) return false;
                return true;
            });
            string res = outcome == CombatOutcome.AttackerWins ? "自軍の勝ち" : outcome == CombatOutcome.DefenderWins ? "自軍の負け" : "相打ち";
            Note(notes, b, move.Ply, NoteKind.Combat, "自軍 " + OwnLabel(myPiece) + " で攻撃して" + res + " → " + (gone.Count > 0 ? Names(gone) + " を除外" : "候補変化なし"));
            if (outcome != CombatOutcome.DefenderWins)
            {
                b.Node = -1;
                Note(notes, b, move.Ply, NoteKind.Removed, "盤上から除去（正体は非公開のまま）");
            }
        }

        private void MarkRemovedIf(EnemyBelief b, CombatOutcome outcome, ObservedMove move, List<Tuple<EnemyBelief, BeliefNote>> notes)
        {
            b.Node = -1;
            Note(notes, b, move.Ply, NoteKind.Removed, "盤上から除去（正体は非公開のまま）");
        }

        /// <summary>Own piece's fighting strength at the moment it was attacked (flag -> piece behind it).</summary>
        private PieceType? OwnStrengthAt(OwnPieceView piece, ObservedMove move)
        {
            if (piece.Type != PieceType.Flag) return piece.Type;
            int back = FlagRule.BackingCell(move.To, Me);
            if (back < 0 || move.OwnersBefore[back] != (sbyte)Me || back == move.From) return null;
            int id = pieceAt[back];
            OwnPieceView p;
            return id >= 0 && own.TryGetValue(id, out p) ? p.Type : (PieceType?)null;
        }

        private void ApplyToBoard(ObservedMove move)
        {
            pieceAt[move.From] = -1;
            var c = move.Combat;
            if (c == null) { pieceAt[move.To] = move.PieceId; Relocate(move.PieceId, move.To); return; }
            switch (c.Outcome)
            {
                case CombatOutcome.AttackerWins:
                    pieceAt[move.To] = move.PieceId; Relocate(move.PieceId, move.To); Relocate(c.DefenderId, -1); break;
                case CombatOutcome.DefenderWins:
                    Relocate(move.PieceId, -1); break;
                default:
                    pieceAt[move.To] = -1; Relocate(move.PieceId, -1); Relocate(c.DefenderId, -1); break;
            }
        }

        private void Relocate(int id, int node)
        {
            EnemyBelief b;
            if (beliefs.TryGetValue(id, out b)) b.Node = node;
        }

        // ------------------------------------------------------------------
        // Priors
        // ------------------------------------------------------------------

        private void ApplyInitialPosition(EnemyBelief b)
        {
            int node = b.InitialNode;
            int depth = BoardGraph.Depth(node, EnemySide);
            if (BoardGraph.IsArmEnd(node))
            {
                var gone = Exclude(b, t => t == PieceType.Mine || t == PieceType.Flag);
                b.Notes.Add(new BeliefNote(0, NoteKind.Excluded, "初期位置が突入口 → " + Names(gone) + " を除外（配置ルール）"));
            }
            if (depth == 0)
            {
                Exclude(b, t => t == PieceType.Flag);
                b.Notes.Add(new BeliefNote(0, NoteKind.Excluded, "初期位置が最後列 → 軍旗を除外（配置ルール）"));
            }
            // Weak priors for how people usually set up (kept small on purpose).
            bool hq = BoardGraph.IsHeadquarters(node, EnemySide);
            Soft(b, t =>
            {
                switch (depth)
                {
                    case 0:
                        if (t == PieceType.Mine) return hq ? 0.55 : 0.25;
                        if (t == PieceType.General || t == PieceType.LieutenantGeneral) return hq ? 0.15 : 0.05;
                        if (t == PieceType.Tank || t == PieceType.Cavalry) return -0.2;
                        return 0;
                    case 1:
                        if (t == PieceType.Flag) return 0.25;
                        if (t == PieceType.Mine) return 0.1;
                        return 0;
                    case 3:
                        if (t == PieceType.Tank || t == PieceType.Cavalry) return 0.25;
                        if (t == PieceType.Engineer || t >= PieceType.Captain && t <= PieceType.SecondLieutenant) return 0.12;
                        if (t == PieceType.General || t == PieceType.Spy || t == PieceType.Mine) return -0.2;
                        return 0;
                    default:
                        return 0;
                }
            });
            string where = hq ? "総司令部" : depth == 0 ? "最後列" : depth == 3 ? "最前列" : (4 - depth) + "列目";
            b.Notes.Add(new BeliefNote(0, NoteKind.Prior, "初期位置（" + where + "）による弱い事前分布"));
        }

        // ------------------------------------------------------------------
        // Inference helpers
        // ------------------------------------------------------------------

        private static List<PieceType> Exclude(EnemyBelief b, Func<PieceType, bool> reject)
        {
            var gone = new List<PieceType>();
            foreach (var t in PieceCatalog.AllTypes)
                if (b.Allowed[(int)t] && reject(t)) { b.Allowed[(int)t] = false; gone.Add(t); }
            if (!b.Allowed.Any(a => a))
            {
                // Contradiction (should not happen with correct rules). Keep the model usable.
                foreach (var t in gone) b.Allowed[(int)t] = true;
                return new List<PieceType>();
            }
            return gone;
        }

        private static void Soft(EnemyBelief b, Func<PieceType, double> delta)
        {
            foreach (var t in PieceCatalog.AllTypes) b.LogWeight[(int)t] += delta(t);
        }

        private static void Note(List<Tuple<EnemyBelief, BeliefNote>> notes, EnemyBelief b, int ply, NoteKind kind, string text)
        {
            var n = new BeliefNote(ply, kind, text);
            b.Notes.Add(n);
            notes.Add(Tuple.Create(b, n));
        }

        /// <summary>
        /// Reference to one of the CPU's own pieces inside a note: "{O:id}". The monitor renders it as
        /// "C#n" and reveals the kind only when the user enables the spoiler switch. "{S:...}" marks
        /// any other text that would reveal CPU piece kinds.
        /// </summary>
        public static string OwnLabel(OwnPieceView p) { return "{O:" + p.Id + "}"; }

        private static string Names(IEnumerable<PieceType> types)
        {
            var list = types.ToList();
            if (list.Count == 0) return "なし";
            if (list.Count > 6) return list.Count + "種（" + string.Join("・", list.Take(4).Select(PieceCatalog.JapaneseName)) + "…）";
            return string.Join("・", list.Select(PieceCatalog.JapaneseName));
        }

        private static string DescribeShape(ObservedMove move, Side mover)
        {
            int fx = BoardGraph.X(move.From), tx = BoardGraph.X(move.To);
            var sb = new StringBuilder();
            sb.Append(BoardGraph.Describe(move.From)).Append("→").Append(BoardGraph.Describe(move.To)).Append(" ");
            int steps = move.Path.Length;
            if (move.Jumped > 0) sb.Append("駒を").Append(move.Jumped).Append("枚飛び越え");
            else if (BoardGraph.IsCell(move.From) && BoardGraph.IsCell(move.To) && BoardGraph.InCamp(move.From, mover) != BoardGraph.InCamp(move.To, mover))
                sb.Append("突入口を通らず陣地を越えた");
            else if (steps >= 2 && fx == tx)
            {
                int dy = BoardGraph.IsCell(move.To) && BoardGraph.IsCell(move.From) ? (BoardGraph.Y(move.To) - BoardGraph.Y(move.From)) * mover.Forward() : 1;
                sb.Append(steps).Append("マス").Append(dy > 0 ? "前進" : "後退");
            }
            else if (steps >= 2) sb.Append(steps).Append("マス横移動");
            else sb.Append("1マス移動");
            return sb.ToString();
        }

        private double[][] Snapshot()
        {
            return beliefs.Values.OrderBy(b => b.Id).Select(b => (double[])b.Probability.Clone()).ToArray();
        }

        /// <summary>
        /// Iterative proportional fitting of weights (piece x kind) to the known army counts.
        /// Adds a "rebalance" note to pieces whose estimate moved noticeably without direct evidence.
        /// </summary>
        private void Solve(int ply, HashSet<int> touched, double[][] before)
        {
            var list = beliefs.Values.OrderBy(b => b.Id).ToList();
            int n = list.Count, k = PieceCatalog.KindCount;
            var m = new double[n, k];
            for (int i = 0; i < n; i++)
            {
                double maxLog = double.NegativeInfinity;
                for (int t = 0; t < k; t++) if (list[i].Allowed[t]) maxLog = Math.Max(maxLog, list[i].LogWeight[t]);
                for (int t = 0; t < k; t++)
                    m[i, t] = list[i].Allowed[t] ? Math.Exp(list[i].LogWeight[t] - maxLog) : 0.0;
            }
            for (int iter = 0; iter < 200; iter++)
            {
                double change = 0;
                for (int i = 0; i < n; i++)
                {
                    double s = 0;
                    for (int t = 0; t < k; t++) s += m[i, t];
                    if (s <= 0) continue;
                    for (int t = 0; t < k; t++) m[i, t] /= s;
                }
                for (int t = 0; t < k; t++)
                {
                    double s = 0;
                    for (int i = 0; i < n; i++) s += m[i, t];
                    if (s <= 0) continue;
                    double f = PieceCatalog.Count((PieceType)t) / s;
                    change = Math.Max(change, Math.Abs(f - 1));
                    for (int i = 0; i < n; i++) m[i, t] *= f;
                }
                if (change < 1e-7) break;
            }
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                for (int t = 0; t < k; t++) s += m[i, t];
                for (int t = 0; t < k; t++) list[i].Probability[t] = s > 0 ? m[i, t] / s : 0;
            }
            if (before == null) return;
            for (int i = 0; i < n; i++)
            {
                if (touched.Contains(list[i].Id)) continue;
                int biggest = -1;
                double delta = 0;
                for (int t = 0; t < k; t++)
                {
                    double d = Math.Abs(list[i].Probability[t] - before[i][t]);
                    if (d > delta) { delta = d; biggest = t; }
                }
                if (delta >= 0.12)
                {
                    var name = PieceCatalog.JapaneseName((PieceType)biggest);
                    var note = new BeliefNote(ply, NoteKind.Rebalance,
                        "他の駒の情報による再配分（" + name + " " + Math.Round(before[i][biggest] * 100) + "%→" + Math.Round(list[i].Probability[biggest] * 100) + "%）");
                    note.After = list[i].TopSummary();
                    list[i].Notes.Add(note);
                }
            }
        }

        /// <summary>Expected remaining enemy count of a kind (alive pieces only).</summary>
        public double ExpectedAlive(PieceType t)
        {
            double s = 0;
            foreach (var b in beliefs.Values) if (b.Alive) s += b.P(t);
            return s;
        }
    }
}
