using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;

namespace MilitaryShogi.Cpu
{
    /// <summary>Tunable play personality. Derived from the formation style so each 配置思想 also plays a little differently.</summary>
    public sealed class CpuPersonality
    {
        public double ProgressWeight = 1.0;
        public double DefenseWeight = 1.0;
        public double RiskAversion = 1.0;
        public double OpportunityWeight = 1.0;
        public double InformationWeight = 1.0;

        // Decision precision (1.0.0 values are the defaults = 「中」).
        /// <summary>Read the opponent's reply (2-ply). 「弱」 turns this off.</summary>
        public bool LookaheadEnabled = true;
        /// <summary>Weight of the opponent's most damaging reply in the 2-ply read.</summary>
        public double ReplyWeight = 0.7;
        /// <summary>Moves within this score of the best are considered near-equal.</summary>
        public double NearMargin = 0.15;
        /// <summary>Softmax temperature for choosing among near-equal moves.</summary>
        public double Temperature = 0.06;
        /// <summary>At most this many near-equal moves are considered.</summary>
        public int MaxNearCandidates = int.MaxValue;

        public static CpuPersonality For(FormationStyle style)
        {
            var p = new CpuPersonality();
            switch (style)
            {
                case FormationStyle.Defensive: p.ProgressWeight = 0.8; p.DefenseWeight = 1.3; p.RiskAversion = 1.25; break;
                case FormationStyle.Aggressive: p.ProgressWeight = 1.3; p.DefenseWeight = 0.9; p.RiskAversion = 0.8; p.OpportunityWeight = 1.2; break;
                case FormationStyle.Trap: p.ProgressWeight = 0.9; p.OpportunityWeight = 1.25; p.InformationWeight = 1.2; break;
                case FormationStyle.Mobile: p.ProgressWeight = 1.15; p.InformationWeight = 1.3; break;
            }
            return p;
        }
    }

    /// <summary>
    /// Decision making from beliefs only.
    ///
    /// For every legal move the brain builds the resulting position(s) – one per
    /// possible combat outcome, weighted by the outcome probabilities implied by the
    /// belief about the target – and scores them with a static evaluation Φ:
    ///   Material     Σ own values − Σ expected enemy values (belief-weighted)
    ///   Progress     officers (大将〜少佐) closing in on the enemy headquarters
    ///   Defense      danger of enemy officers near our headquarters, incl. the
    ///                probability that some enemy can capture it next move
    ///   Safety       expected loss if the opponent attacks one of our pieces next
    ///                move (one-ply opponent model using the beliefs)
    ///   Opportunity  best attack we could make next move
    /// plus Information (expected entropy reduction of a probing attack) and a
    /// repetition penalty. Near-equal moves are chosen by the decision seed.
    /// </summary>
    public sealed class CpuBrain
    {
        private const double WinScore = 10000.0;
        private readonly Side me;
        private readonly Side enemy;
        private readonly CpuPersonality personality;
        private readonly int decisionSeed;

        public CpuBrain(Side me, int decisionSeed, CpuPersonality personality)
        {
            this.me = me;
            enemy = me.Opponent();
            this.decisionSeed = decisionSeed;
            this.personality = personality ?? new CpuPersonality();
        }

        // ------------------------------------------------------------------
        // Position model (CPU side only)
        // ------------------------------------------------------------------

        private sealed class Pos
        {
            public sbyte[] Owners;
            public int[] PieceAt;
            public List<int> Mine;      // own alive piece ids
            public List<int> Theirs;    // enemy alive piece ids
            public int[] NodeById;      // piece id -> node (-1 removed)

            public Pos Clone()
            {
                return new Pos
                {
                    Owners = (sbyte[])Owners.Clone(), PieceAt = (int[])PieceAt.Clone(), Mine = new List<int>(Mine),
                    Theirs = new List<int>(Theirs), NodeById = (int[])NodeById.Clone(),
                };
            }
        }

        private Dictionary<int, OwnPieceView> own;
        private CpuKnowledge knowledge;
        private readonly Dictionary<int, double> enemyValue = new Dictionary<int, double>();
        private double[] values;

        public DecisionReport Decide(PlayerView view, CpuKnowledge knowledge, FormationStyle style)
        {
            var watch = Stopwatch.StartNew();
            this.knowledge = knowledge;
            own = view.Own.ToDictionary(p => p.Id);
            values = PieceValues(knowledge);
            enemyValue.Clear();
            foreach (var b in knowledge.Enemies) enemyValue[b.Id] = Expected(b, t => values[(int)t]);

            int maxId = view.Own.Concat<object>(view.Enemy).Count();
            var basePos = new Pos
            {
                Owners = (sbyte[])view.Owners.Clone(), PieceAt = new int[BoardGraph.NodeCount], Mine = new List<int>(),
                Theirs = new List<int>(), NodeById = new int[maxId],
            };
            for (int i = 0; i < basePos.PieceAt.Length; i++) basePos.PieceAt[i] = -1;
            for (int i = 0; i < basePos.NodeById.Length; i++) basePos.NodeById[i] = -1;
            foreach (var p in view.Own) if (p.Alive) { basePos.PieceAt[p.Node] = p.Id; basePos.NodeById[p.Id] = p.Node; basePos.Mine.Add(p.Id); }
            foreach (var e in view.Enemy) if (e.Alive) { basePos.PieceAt[e.Node] = e.Id; basePos.NodeById[e.Id] = e.Node; basePos.Theirs.Add(e.Id); }

            var report = new DecisionReport { Ply = view.Ply + 1, Side = me, Style = style, DecisionSeed = decisionSeed, Own = view.Own };
            var phi0 = Evaluate(basePos);
            var lastMoves = LastMoves(view);

            foreach (int id in basePos.Mine)
            {
                var piece = own[id];
                foreach (var target in MoveRules.Generate(piece.Type, me, piece.Node, basePos.Owners))
                {
                    var c = new CandidateMove
                    {
                        Command = new MoveCommand(id, target.To), From = piece.Node, PieceNumber = piece.Number, PieceType = piece.Type,
                    };
                    ScoreCandidate(c, target, basePos, phi0, lastMoves);
                    report.Candidates.Add(c);
                }
            }

            report.Candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            // 2-ply: re-score every move by reading the opponent's best reply (all candidates, so the
            // correction is applied evenly).
            if (personality.LookaheadEnabled)
                foreach (var c in report.Candidates) ReadReply(c, basePos, phi0);
            report.Candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            report.Chosen = Choose(report.Candidates, view.Ply);
            report.Reason = Explain(report.Chosen, report.Candidates);
            report.Considerations.AddRange(Considerations(basePos));
            foreach (var b in knowledge.Enemies)
                report.Beliefs.Add(new EnemyBeliefSnapshot
                {
                    Id = b.Id, Number = b.Number, Node = b.Node, Alive = b.Alive, Probability = (double[])b.Probability.Clone(),
                    Allowed = (bool[])b.Allowed.Clone(), Notes = new List<BeliefNote>(b.Notes), Entropy = b.Entropy(),
                });
            report.ThinkMilliseconds = watch.Elapsed.TotalMilliseconds;
            return report;
        }

        // ------------------------------------------------------------------
        // Candidate scoring
        // ------------------------------------------------------------------

        private void ScoreCandidate(CandidateMove c, MoveTarget target, Pos basePos, ScoreTerms phi0, Dictionary<int, int> lastFrom)
        {
            int id = c.Command.PieceId;
            var type = own[id].Type;
            var terms = new ScoreTerms();
            int lf;
            if (lastFrom.TryGetValue(id, out lf) && lf == target.To) terms.Repetition = -0.35;

            if (!target.IsAttack)
            {
                var after = basePos.Clone();
                MoveTo(after, id, c.From, target.To);
                terms = terms + (Evaluate(after) - phi0);
                if (IsVictory(type, target.To)) terms.Victory = WinScore;
                c.Note = IsVictory(type, target.To) ? "敵総司令部を占領" : "";
            }
            else
            {
                int enemyId = basePos.PieceAt[target.To];
                var b = knowledge.Belief(enemyId);
                c.TargetEnemyId = enemyId;
                c.TargetEnemyNumber = b.Number;
                double pw, pl, pt;
                OutcomeProbabilities(type, b, target.To, basePos, out pw, out pl, out pt);
                c.WinP = pw; c.LoseP = pl; c.TieP = pt;

                var sum = new ScoreTerms();
                if (pw > 0)
                {
                    var after = basePos.Clone();
                    RemovePiece(after, enemyId, target.To);
                    MoveTo(after, id, c.From, target.To);
                    sum = sum + pw * (Evaluate(after) - phi0);
                    if (IsVictory(type, target.To)) sum.Victory += pw * WinScore;
                }
                if (pl > 0)
                {
                    var after = basePos.Clone();
                    RemovePiece(after, id, c.From);
                    sum = sum + pl * (Evaluate(after) - phi0);
                }
                if (pt > 0)
                {
                    var after = basePos.Clone();
                    RemovePiece(after, id, c.From);
                    RemovePiece(after, enemyId, target.To);
                    sum = sum + pt * (Evaluate(after) - phi0);
                }
                terms = terms + sum;
                terms.Information = personality.InformationWeight * InformationGain(type, b, target.To, basePos);
                c.Note = "敵#" + b.Number + "へ攻撃 勝" + Pct(pw) + " 分" + Pct(pt) + " 負" + Pct(pl) + "（推定: " + b.TopSummary(3) + "）";
            }
            c.Terms = terms;
            c.Score = terms.Total;
        }


        /// <summary>One possible result of our move: probability and position.</summary>
        private struct Branch
        {
            public double P;
            public Pos Pos;
        }

        private List<Branch> Branches(CandidateMove c, Pos basePos)
        {
            var list = new List<Branch>();
            int id = c.Command.PieceId, to = c.Command.To;
            if (c.TargetEnemyId < 0)
            {
                var after = basePos.Clone();
                MoveTo(after, id, c.From, to);
                list.Add(new Branch { P = 1, Pos = after });
                return list;
            }
            if (c.WinP > 0)
            {
                var after = basePos.Clone();
                RemovePiece(after, c.TargetEnemyId, to);
                MoveTo(after, id, c.From, to);
                list.Add(new Branch { P = c.WinP, Pos = after });
            }
            if (c.LoseP > 0)
            {
                var after = basePos.Clone();
                RemovePiece(after, id, c.From);
                list.Add(new Branch { P = c.LoseP, Pos = after });
            }
            if (c.TieP > 0)
            {
                var after = basePos.Clone();
                RemovePiece(after, id, c.From);
                RemovePiece(after, c.TargetEnemyId, to);
                list.Add(new Branch { P = c.TieP, Pos = after });
            }
            return list;
        }

        /// <summary>
        /// Re-score a candidate with the opponent's reply. The opponent knows its own kinds, so for
        /// each enemy piece and each movement class it could have, its reply is evaluated with
        /// the kinds of that class (belief-weighted). The most damaging reply is assumed to be
        /// played with weight <see cref="CpuPersonality.ReplyWeight"/> (the opponent does not know our kinds,
        /// so it does not always find it). Capturing our headquarters is scored as a loss.
        /// </summary>
        private void ReadReply(CandidateMove c, Pos basePos, ScoreTerms phi0)
        {
            if (c.Terms.Victory > 0) return;
            double oneply = c.Score - c.Terms.Information - c.Terms.Repetition;
            double total = 0;
            string worstNote = null;
            double worstDrop = 0;
            foreach (var br in Branches(c, basePos))
            {
                double stay = Evaluate(br.Pos).Total;
                double worst = stay;
                string note = null;
                foreach (int eid in br.Pos.Theirs.ToList())
                {
                    var b = knowledge.Belief(eid);
                    int node = NodeOf(br.Pos, eid);
                    foreach (var cls in replyClasses)
                    {
                        double pc = 0;
                        foreach (var k in PieceCatalog.AllTypes) if (PieceCatalog.MoveClassOf(k) == cls) pc += b.P(k);
                        if (pc < 0.01) continue;
                        replyBuffer.Clear();
                        MoveRules.Generate(cls, enemy, node, br.Pos.Owners, replyBuffer);
                        foreach (var target in replyBuffer.ToArray())
                        {
                            if (!IsRelevantReply(b, node, target)) continue;
                            double v = 0;
                            foreach (var k in PieceCatalog.AllTypes)
                            {
                                if (PieceCatalog.MoveClassOf(k) != cls) continue;
                                double pk = b.P(k);
                                if (pk <= 0) continue;
                                v += pk / pc * ReplyValue(br.Pos, eid, node, target.To, k);
                            }
                            double value = pc * v + (1 - pc) * stay;
                            if (value < worst)
                            {
                                worst = value;
                                note = "敵#" + b.Number + " " + BoardGraph.Describe(node) + "→" + BoardGraph.Describe(target.To);
                            }
                        }
                    }
                }
                double read = stay + personality.ReplyWeight * (worst - stay);
                total += br.P * read;
                if (stay - worst > worstDrop) { worstDrop = stay - worst; worstNote = note; }
            }
            double twoPly = total - phi0.Total;
            c.Terms.Lookahead = twoPly - oneply;
            c.Score = c.Terms.Total;
            if (worstNote != null && worstDrop > 0.3) c.ReplyNote = "想定される応手: " + worstNote + "（評価 -" + worstDrop.ToString("0.0") + "）";
        }

        private readonly List<MoveTarget> replyBuffer = new List<MoveTarget>();
        private static readonly MoveClass[] replyClasses = { MoveClass.Step, MoveClass.Charger, MoveClass.Slider, MoveClass.Flyer };

        /// <summary>Replies worth reading: attacks on our pieces and officer-like pieces closing in on our HQ.</summary>
        private bool IsRelevantReply(EnemyBelief b, int from, MoveTarget target)
        {
            if (target.IsAttack) return true;
            if (BoardGraph.IsHeadquarters(target.To, me)) return true;
            double officer = 0;
            foreach (var k in PieceCatalog.AllTypes) if (PieceCatalog.CanCaptureHeadquarters(k)) officer += b.P(k);
            if (officer < 0.1) return false;
            int d = BoardGraph.DistanceToHeadquarters(target.To, me);
            return d <= 3 && d < BoardGraph.DistanceToHeadquarters(from, me);
        }

        /// <summary>Our evaluation after enemy piece <paramref name="eid"/> of kind <paramref name="kind"/> moves to <paramref name="to"/>.</summary>
        private double ReplyValue(Pos pos, int eid, int from, int to, PieceType kind)
        {
            var after = pos.Clone();
            int victim = after.PieceAt[to];
            bool enters = true;
            if (victim >= 0)
            {
                switch (CombatTable.Resolve(kind, OwnStrength(after, victim)))
                {
                    case CombatOutcome.AttackerWins: RemovePiece(after, victim, to); break;
                    case CombatOutcome.DefenderWins: RemovePiece(after, eid, from); enters = false; break;
                    default: RemovePiece(after, victim, to); RemovePiece(after, eid, from); enters = false; break;
                }
            }
            if (enters)
            {
                if (PieceCatalog.CanCaptureHeadquarters(kind) && BoardGraph.IsHeadquarters(to, me)) return -WinScore;
                after.PieceAt[from] = -1; after.Owners[from] = MoveRules.Empty;
                after.PieceAt[to] = eid; after.Owners[to] = (sbyte)enemy;
                after.NodeById[eid] = to;
            }
            return Evaluate(after).Total;
        }

        private bool IsVictory(PieceType type, int node)
        {
            return PieceCatalog.CanCaptureHeadquarters(type) && BoardGraph.IsHeadquarters(node, enemy);
        }

        private void MoveTo(Pos p, int id, int from, int to)
        {
            p.PieceAt[from] = -1; p.Owners[from] = MoveRules.Empty;
            p.PieceAt[to] = id; p.Owners[to] = (sbyte)me;   // only our own moves are simulated
            p.NodeById[id] = to;
        }

        private static void RemovePiece(Pos p, int id, int node)
        {
            if (p.PieceAt[node] == id) { p.PieceAt[node] = -1; p.Owners[node] = MoveRules.Empty; }
            p.NodeById[id] = -1;
            p.Mine.Remove(id);
            p.Theirs.Remove(id);
        }

        // ------------------------------------------------------------------
        // Beliefs -> combat probabilities
        // ------------------------------------------------------------------

        /// <summary>Win/lose/tie probabilities of our <paramref name="attacker"/> against enemy piece <paramref name="b"/> standing on <paramref name="node"/>.</summary>
        private void OutcomeProbabilities(PieceType attacker, EnemyBelief b, int node, Pos pos, out double win, out double lose, out double tie)
        {
            win = lose = tie = 0;
            foreach (var t in PieceCatalog.AllTypes)
            {
                double p = b.P(t);
                if (p <= 0) continue;
                if (t == PieceType.Flag)
                {
                    int back = FlagRule.BackingCell(node, enemy);
                    int backerId = back >= 0 && pos.Owners[back] == (sbyte)enemy ? pos.PieceAt[back] : -1;
                    var backer = backerId >= 0 ? knowledge.Belief(backerId) : null;
                    if (backer == null) { win += p; continue; }
                    double norm = 0;
                    foreach (var bt in PieceCatalog.AllTypes) if (bt != PieceType.Flag) norm += backer.P(bt);
                    if (norm <= 0) { win += p; continue; }
                    foreach (var bt in PieceCatalog.AllTypes)
                    {
                        if (bt == PieceType.Flag) continue;
                        double q = p * backer.P(bt) / norm;
                        Add(CombatTable.Resolve(attacker, bt), q, ref win, ref lose, ref tie);
                    }
                    continue;
                }
                Add(CombatTable.Resolve(attacker, t), p, ref win, ref lose, ref tie);
            }
        }

        private static void Add(CombatOutcome o, double p, ref double win, ref double lose, ref double tie)
        {
            if (o == CombatOutcome.AttackerWins) win += p;
            else if (o == CombatOutcome.DefenderWins) lose += p;
            else tie += p;
        }

        /// <summary>Expected entropy reduction (bits) of attacking, scaled by how much the target matters.</summary>
        private double InformationGain(PieceType attacker, EnemyBelief b, int node, Pos pos)
        {
            double h = b.Entropy();
            if (h < 0.3) return 0;
            var byOutcome = new double[3][];
            for (int o = 0; o < 3; o++) byOutcome[o] = new double[PieceCatalog.KindCount];
            foreach (var t in PieceCatalog.AllTypes)
            {
                double p = b.P(t);
                if (p <= 0 || t == PieceType.Flag) continue;   // flag outcome depends on its backer; treated as noise
                byOutcome[(int)CombatTable.Resolve(attacker, t)][(int)t] += p;
            }
            double expected = 0;
            for (int o = 0; o < 3; o++)
            {
                double po = byOutcome[o].Sum();
                if (po <= 0) continue;
                double ho = 0;
                foreach (double q in byOutcome[o]) if (q > 0) { double r = q / po; ho -= r * Math.Log(r, 2); }
                expected += po * ho;
            }
            double gain = Math.Max(0, h - expected);
            int d = BoardGraph.DistanceToHeadquarters(node, me);
            double importance = d <= 2 ? 2.0 : d <= 4 ? 1.3 : 1.0;
            // Probing costs our piece; information is worth more when the prober is cheap.
            double cheapness = values[(int)attacker] <= 3.5 ? 1.0 : 0.5;
            return 0.25 * gain * importance * cheapness;
        }

        // ------------------------------------------------------------------
        // Static evaluation Φ
        // ------------------------------------------------------------------

        private ScoreTerms Evaluate(Pos pos)
        {
            var t = new ScoreTerms();
            foreach (int id in pos.Mine) t.Material += values[(int)own[id].Type];
            foreach (int id in pos.Theirs) t.Material -= enemyValue[id];

            foreach (int id in pos.Mine)
            {
                var type = own[id].Type;
                int node = NodeOf(pos, id);
                double w = ProgressWeight(type);
                if (w <= 0) continue;
                int d = Math.Min(12, BoardGraph.DistanceToHeadquarters(node, enemy));
                // Linear so every step toward the enemy HQ counts, plus a sharp bonus close to it.
                double closeness = 0.15 * (12 - d) + (d <= 3 ? 1.2 / Math.Max(1, d) : 0.0);
                t.Progress += personality.ProgressWeight * w * closeness;
            }

            // Enemy reach: node -> list of (enemy id, kind) that could move there next turn.
            var reach = EnemyReach(pos);
            var hq = BoardGraph.Headquarters(me);
            double danger = HeadquartersDanger(pos, hq);
            double immediate = 0;
            foreach (int h in hq)
            {
                List<Reach> list;
                if (!reach.TryGetValue(h, out list)) continue;
                int occupant = pos.PieceAt[h];
                var byEnemy = new Dictionary<int, double>();
                foreach (var r in list)
                {
                    if (!PieceCatalog.CanCaptureHeadquarters(r.Kind)) continue;
                    double p = knowledge.Belief(r.EnemyId).P(r.Kind);
                    if (occupant >= 0 && pos.Mine.Contains(occupant))
                    {
                        var strength = OwnStrength(pos, occupant);
                        if (CombatTable.Resolve(r.Kind, strength) != CombatOutcome.AttackerWins) continue;
                    }
                    double cur;
                    byEnemy.TryGetValue(r.EnemyId, out cur);
                    byEnemy[r.EnemyId] = cur + p;
                }
                foreach (var v in byEnemy.Values) immediate = Math.Max(immediate, Math.Min(1.0, v));
            }
            // Guards: when enemy officers are near, own pieces next to our HQ are worth more.
            double threat = 0;
            foreach (int id in pos.Theirs)
            {
                var b = knowledge.Belief(id);
                double officer = 0;
                foreach (var k in PieceCatalog.AllTypes) if (PieceCatalog.CanCaptureHeadquarters(k)) officer += b.P(k);
                int d = BoardGraph.DistanceToHeadquarters(NodeOf(pos, id), me);
                threat += officer * Math.Max(0, 5 - d) / 5.0;
            }
            double guards = 0;
            if (threat > 0)
                foreach (int id in pos.Mine)
                {
                    int d = BoardGraph.DistanceToHeadquarters(NodeOf(pos, id), me);
                    if (d > 1) continue;
                    var type = own[id].Type;
                    guards += type == PieceType.Mine ? 1.0 : Math.Min(1.0, values[(int)type] / 8.0);
                }
            t.Defense = personality.DefenseWeight * (0.6 * Math.Min(threat, 3.0) * Math.Min(guards, 3.0) - danger - 250.0 * immediate);

            // Safety: expected loss if the opponent attacks one of our pieces next move.
            var risks = new List<double>();
            foreach (int id in pos.Mine)
            {
                int node = NodeOf(pos, id);
                List<Reach> list;
                if (!reach.TryGetValue(node, out list)) continue;
                double mineValue = values[(int)own[id].Type];
                var strength = OwnStrength(pos, id);
                var byEnemy = new Dictionary<int, double>();
                foreach (var r in list)
                {
                    double p = knowledge.Belief(r.EnemyId).P(r.Kind);
                    if (p <= 0) continue;
                    double loss;
                    switch (CombatTable.Resolve(r.Kind, strength))
                    {
                        case CombatOutcome.AttackerWins: loss = mineValue; break;
                        case CombatOutcome.Tie: loss = mineValue - values[(int)r.Kind]; break;
                        default: loss = -values[(int)r.Kind]; break;
                    }
                    double attackRate = r.Kind <= PieceType.LieutenantColonel || r.Kind == PieceType.Airplane || r.Kind == PieceType.Tank ? 0.55 : 0.35;
                    double cur;
                    byEnemy.TryGetValue(r.EnemyId, out cur);
                    byEnemy[r.EnemyId] = cur + p * attackRate * loss;
                }
                double worst = 0;
                foreach (var v in byEnemy.Values) worst = Math.Max(worst, v);
                if (worst > 0) risks.Add(worst);
            }
            if (risks.Count > 0)
            {
                risks.Sort();
                double max = risks[risks.Count - 1];
                t.Safety = -personality.RiskAversion * (max + 0.3 * (risks.Sum() - max));
            }

            // Opportunity: our best attack next move.
            double bestAttack = 0;
            foreach (int id in pos.Mine)
            {
                var type = own[id].Type;
                if (!PieceCatalog.IsMobile(type)) continue;
                foreach (var target in MoveRules.Generate(type, me, NodeOf(pos, id), pos.Owners))
                {
                    if (!target.IsAttack) continue;
                    var b = knowledge.Belief(pos.PieceAt[target.To]);
                    if (b == null) continue;
                    double w, l, ti;
                    OutcomeProbabilities(type, b, target.To, pos, out w, out l, out ti);
                    double ev = w * enemyValue[b.Id] - l * values[(int)type] + ti * (enemyValue[b.Id] - values[(int)type]);
                    if (IsVictory(type, target.To)) ev += w * 50;
                    bestAttack = Math.Max(bestAttack, ev);
                }
            }
            t.Opportunity = personality.OpportunityWeight * 0.35 * bestAttack;
            return t;
        }

        /// <summary>
        /// Danger of losing our headquarters within a few moves. For every enemy piece that could
        /// be an officer (大将〜少佐), a shortest path to each HQ cell is computed where entering an
        /// empty node costs 1 and entering one of our pieces costs 1 + 2 x (chance that this enemy,
        /// assuming it is an officer, does NOT beat it). The danger is the probability that it is an
        /// officer able to take the HQ cell, times a weight that grows sharply as the path shortens,
        /// reduced when one of our pieces that beats it is close enough to intercept.
        /// Path length 1 is handled separately as an immediate threat.
        /// </summary>
        private double HeadquartersDanger(Pos pos, int[] hq)
        {
            double total = 0;
            foreach (int id in pos.Theirs)
            {
                var b = knowledge.Belief(id);
                int en = NodeOf(pos, id);
                double officer = 0;
                foreach (var k in PieceCatalog.AllTypes) if (PieceCatalog.CanCaptureHeadquarters(k)) officer += b.P(k);
                if (officer < 0.02 || BoardGraph.DistanceToHeadquarters(en, me) > 5) continue;

                var cost = PathCosts(pos, en, b, officer);
                double worst = 0;
                foreach (int h in hq)
                {
                    double d = cost[h];
                    if (d <= 1.01 || d > 6) continue;
                    int occupant = pos.PieceAt[h];
                    bool guarded = occupant >= 0 && pos.Mine.Contains(occupant);
                    var guard = guarded ? OwnStrength(pos, occupant) : null;
                    double p = 0;
                    foreach (var k in PieceCatalog.AllTypes)
                    {
                        if (!PieceCatalog.CanCaptureHeadquarters(k)) continue;
                        if (guarded && CombatTable.Resolve(k, guard) != CombatOutcome.AttackerWins) continue;
                        p += b.P(k);
                    }
                    double w = 0.45 * Math.Exp(-0.8 * (d - 2));
                    worst = Math.Max(worst, 40.0 * p * Math.Min(1.0, w));
                }
                if (worst <= 0) continue;

                // Interception: less dangerous when one of ours that beats it is close.
                double cover = 0;
                foreach (int mid in pos.Mine)
                {
                    var type = own[mid].Type;
                    if (!PieceCatalog.IsMobile(type)) continue;
                    int d = BoardGraph.Distance(NodeOf(pos, mid), en);
                    double g = d <= 1 ? 0.9 : d == 2 ? 0.6 : d == 3 ? 0.35 : d == 4 ? 0.15 : 0;
                    if (g <= 0) continue;
                    double win = 0;
                    foreach (var k in PieceCatalog.AllTypes)
                        if (k != PieceType.Flag && CombatTable.Resolve(type, k) == CombatOutcome.AttackerWins) win += b.P(k);
                    cover = Math.Max(cover, win * g);
                }
                total += worst * (1.0 - 0.8 * cover);
            }
            return total;
        }

        /// <summary>Dijkstra from an enemy piece; our pieces are costly to pass only if it probably loses to them.</summary>
        private double[] PathCosts(Pos pos, int from, EnemyBelief b, double officer)
        {
            var dist = new double[BoardGraph.NodeCount];
            var done = new bool[BoardGraph.NodeCount];
            for (int i = 0; i < dist.Length; i++) dist[i] = double.MaxValue;
            dist[from] = 0;
            for (int iter = 0; iter < BoardGraph.NodeCount; iter++)
            {
                int u = -1;
                for (int i = 0; i < dist.Length; i++) if (!done[i] && dist[i] < double.MaxValue && (u < 0 || dist[i] < dist[u])) u = i;
                if (u < 0) break;
                done[u] = true;
                if (u != from && pos.Owners[u] != MoveRules.Empty) continue;   // cannot walk past an occupied node
                foreach (int n in BoardGraph.Neighbors(u))
                {
                    if (pos.Owners[n] == (sbyte)enemy) continue;
                    double step = 1;
                    if (pos.Owners[n] == (sbyte)me && !BoardGraph.IsHeadquarters(n, me))
                    {
                        var strength = OwnStrength(pos, pos.PieceAt[n]);
                        double beat = 0;
                        foreach (var k in PieceCatalog.AllTypes)
                            if (PieceCatalog.CanCaptureHeadquarters(k) && CombatTable.Resolve(k, strength) == CombatOutcome.AttackerWins) beat += b.P(k);
                        step += 2.0 * (1.0 - beat / officer);
                    }
                    if (dist[u] + step < dist[n]) dist[n] = dist[u] + step;
                }
            }
            return dist;
        }

        private struct Reach
        {
            public int EnemyId;
            public PieceType Kind;
        }

        private Dictionary<int, List<Reach>> EnemyReach(Pos pos)
        {
            var map = new Dictionary<int, List<Reach>>();
            var buffer = new List<MoveTarget>();
            var classes = new[] { MoveClass.Step, MoveClass.Charger, MoveClass.Slider, MoveClass.Flyer };
            foreach (int id in pos.Theirs)
            {
                var b = knowledge.Belief(id);
                int node = NodeOf(pos, id);
                foreach (var cls in classes)
                {
                    bool any = false;
                    foreach (var k in PieceCatalog.AllTypes) if (PieceCatalog.MoveClassOf(k) == cls && b.P(k) > 0.001) { any = true; break; }
                    if (!any) continue;
                    buffer.Clear();
                    MoveRules.Generate(cls, enemy, node, pos.Owners, buffer);
                    foreach (var target in buffer)
                    {
                        List<Reach> list;
                        if (!map.TryGetValue(target.To, out list)) map[target.To] = list = new List<Reach>();
                        foreach (var k in PieceCatalog.AllTypes)
                            if (PieceCatalog.MoveClassOf(k) == cls && b.P(k) > 0.001) list.Add(new Reach { EnemyId = id, Kind = k });
                    }
                }
            }
            return map;
        }

        private PieceType? OwnStrength(Pos pos, int id)
        {
            var type = own[id].Type;
            if (type != PieceType.Flag) return type;
            int back = FlagRule.BackingCell(NodeOf(pos, id), me);
            if (back < 0 || pos.Owners[back] != (sbyte)me) return null;
            int backer = pos.PieceAt[back];
            return backer >= 0 && own.ContainsKey(backer) ? own[backer].Type : (PieceType?)null;
        }

        private static int NodeOf(Pos pos, int id) { return pos.NodeById[id]; }

        private static double ProgressWeight(PieceType t)
        {
            if (t == PieceType.General) return 0.8;
            if (PieceCatalog.CanCaptureHeadquarters(t)) return 1.0;
            switch (t)
            {
                case PieceType.Airplane: return 0.5;
                case PieceType.Tank: return 0.55;
                case PieceType.Engineer: return 0.45;
                case PieceType.Cavalry: return 0.35;
                case PieceType.Mine:
                case PieceType.Flag: return 0;
                default: return 0.2;
            }
        }

        private static double[] PieceValues(CpuKnowledge k)
        {
            var v = new double[PieceCatalog.KindCount];
            v[(int)PieceType.General] = 12; v[(int)PieceType.LieutenantGeneral] = 9; v[(int)PieceType.MajorGeneral] = 7.5;
            v[(int)PieceType.Colonel] = 6; v[(int)PieceType.LieutenantColonel] = 5.2; v[(int)PieceType.Major] = 4.6;
            v[(int)PieceType.Captain] = 3.4; v[(int)PieceType.FirstLieutenant] = 3.0; v[(int)PieceType.SecondLieutenant] = 2.6;
            v[(int)PieceType.Airplane] = 8; v[(int)PieceType.Tank] = 5.5; v[(int)PieceType.Cavalry] = 2.2;
            v[(int)PieceType.Engineer] = 3 + 0.6 * k.ExpectedAlive(PieceType.Mine);
            v[(int)PieceType.Spy] = 1.5 + 6 * Math.Min(1.0, k.ExpectedAlive(PieceType.General));
            v[(int)PieceType.Mine] = 3; v[(int)PieceType.Flag] = 2;
            return v;
        }

        private static double Expected(EnemyBelief b, Func<PieceType, double> f)
        {
            double s = 0;
            foreach (var t in PieceCatalog.AllTypes) s += b.P(t) * f(t);
            return s;
        }

        private Dictionary<int, int> LastMoves(PlayerView view)
        {
            var map = new Dictionary<int, int>();
            for (int i = view.History.Count - 1, seen = 0; i >= 0 && seen < 4; i--)
            {
                var m = view.History[i];
                if (m.Mover != me) continue;
                seen++;
                if (!map.ContainsKey(m.PieceId)) map[m.PieceId] = m.From;
            }
            return map;
        }

        // ------------------------------------------------------------------
        // Choice and explanation
        // ------------------------------------------------------------------

        private CandidateMove Choose(List<CandidateMove> sorted, int ply)
        {
            if (sorted.Count == 0) return null;
            double best = sorted[0].Score;
            if (best >= WinScore / 2) return sorted[0];
            var near = sorted.Where(c => c.Score >= best - personality.NearMargin).Take(personality.MaxNearCandidates).ToList();
            if (near.Count == 1) return near[0];
            var rng = DeterministicRandom.Derive(decisionSeed, ply, 0xDEC);
            double temperature = personality.Temperature;
            var weights = near.Select(c => Math.Exp((c.Score - best) / temperature)).ToList();
            double r = rng.NextDouble() * weights.Sum();
            for (int i = 0; i < near.Count; i++)
            {
                r -= weights[i];
                if (r <= 0) return near[i];
            }
            return near[near.Count - 1];
        }

        private string Explain(CandidateMove chosen, List<CandidateMove> all)
        {
            if (chosen == null) return "合法手なし";
            string piece = "{O:" + chosen.Command.PieceId + "}";
            if (chosen.Terms.Victory >= WinScore / 2) return piece + " で敵総司令部を占領できる（勝利確定手）。";
            var parts = new List<string>();
            if (chosen.TargetEnemyId >= 0)
                parts.Add(piece + " で" + chosen.Note + "。期待評価 " + chosen.Score.ToString("+0.00;-0.00"));
            else
                parts.Add(piece + " を " + BoardGraph.Describe(chosen.From) + "→" + BoardGraph.Describe(chosen.Command.To) + " へ移動。評価 " + chosen.Score.ToString("+0.00;-0.00"));
            var major = chosen.Terms.Named().Where(kv => Math.Abs(kv.Value) >= 0.005).OrderByDescending(kv => Math.Abs(kv.Value)).Take(3)
                .Select(kv => kv.Key + " " + kv.Value.ToString("+0.00;-0.00"));
            parts.Add("主な要因: " + string.Join(", ", major));
            if (!string.IsNullOrEmpty(chosen.ReplyNote)) parts.Add(chosen.ReplyNote);
            int near = all.Count(c => c != chosen && c.Score >= chosen.Score - personality.NearMargin);
            if (near > 0) parts.Add("僅差の候補 " + near + " 手から Decision Seed で選択");
            return string.Join("\n", parts);
        }

        private IEnumerable<string> Considerations(Pos pos)
        {
            // Highlight the most threatening enemy pieces and the most uncertain ones.
            var threats = pos.Theirs.Select(id => knowledge.Belief(id))
                .Select(b => new { b, d = BoardGraph.DistanceToHeadquarters(b.Node, me), o = Enumerable.Range(0, 6).Sum(k => b.P((PieceType)k)) })
                .Where(x => x.d <= 4).OrderBy(x => x.d).ThenByDescending(x => x.o).Take(3);
            foreach (var x in threats)
                yield return "警戒: 敵#" + x.b.Number + " が自軍司令部まで " + x.d + " 手（佐官以上の確率 " + Pct(x.o) + "）";
            double general = knowledge.ExpectedAlive(PieceType.General);
            yield return "敵大将の残存見込み " + general.ToString("0.00") + " / 敵地雷の残存見込み " + knowledge.ExpectedAlive(PieceType.Mine).ToString("0.00");
        }

        private static string Pct(double p) { return Math.Round(p * 100) + "%"; }
    }
}
