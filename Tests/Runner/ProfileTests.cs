using System;
using System.Collections.Generic;
using System.Linq;
using MilitaryShogi.Cpu;
using MilitaryShogi.Engine;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;
using static MilitaryShogi.Tests.Program;

namespace MilitaryShogi.Tests
{
    /// <summary>1.1.0: CPU strength / temperament and the rule reference used by 「あそびかた」.</summary>
    internal static class ProfileTests
    {
        private static string PlayGame(Func<Side, CpuPlayer> make, int maxPlies, out List<CpuPlayer> players, out Match match)
        {
            var south = make(Side.South);
            var north = make(Side.North);
            players = new List<CpuPlayer> { south, north };
            match = new Match(south.CreateFormation(), north.CreateFormation());
            var log = new List<string>();
            while (match.Status == GameStatus.Playing && match.Ply < maxPlies)
            {
                var cpu = match.ToMove == Side.South ? south : north;
                var r = cpu.Decide(match.GetView(match.ToMove));
                log.Add(r.Chosen.Command + "=" + r.Chosen.Score.ToString("R") + "/" + r.Candidates.Count);
                match.Apply(match.ToMove, r.Chosen.Command);
            }
            return string.Join(";", log);
        }

        /// <summary>「中・バランス」 with the same formation style reproduces the 1.0.0 CPU exactly.</summary>
        public static void StandardEqualsLegacy()
        {
            foreach (FormationStyle style in Enum.GetValues(typeof(FormationStyle)))
            {
                string legacy = PlayGame(s => new CpuPlayer(s, 70 + (int)s, 80 + (int)s, style), 60, out _, out _);
                string standard = PlayGame(s => new CpuPlayer(s, 70 + (int)s, 80 + (int)s, CpuProfile.Standard, style), 60, out _, out _);
                Check(legacy == standard, "中・バランス differs from 1.0.0 for style " + style);
            }
            var p = CpuProfile.Standard.Personality(FormationStyle.Balanced);
            var q = CpuPersonality.For(FormationStyle.Balanced);
            Check(p.LookaheadEnabled && p.NearMargin == q.NearMargin && p.Temperature == q.Temperature && p.ReplyWeight == q.ReplyWeight, "standard precision = 1.0.0");
        }

        public static void StrengthLevels()
        {
            PlayGame(s => new CpuPlayer(s, 11 + (int)s, 12 + (int)s, new CpuProfile(CpuStrength.Weak, 0)), 50, out var weak, out _);
            foreach (var r in weak.SelectMany(c => c.Reports))
                foreach (var c in r.Candidates)
                    Check(c.Terms.Lookahead == 0 && c.ReplyNote == null, "弱 must not read the opponent's reply");
            PlayGame(s => new CpuPlayer(s, 11 + (int)s, 12 + (int)s, new CpuProfile(CpuStrength.Normal, 0)), 50, out var normal, out _);
            Check(normal.SelectMany(c => c.Reports).SelectMany(r => r.Candidates).Any(c => c.Terms.Lookahead != 0), "中 uses the 2-ply read");
            PlayGame(s => new CpuPlayer(s, 11 + (int)s, 12 + (int)s, new CpuProfile(CpuStrength.Strong, 0)), 50, out var strong, out _);
            Check(strong.SelectMany(c => c.Reports).SelectMany(r => r.Candidates).Any(c => c.Terms.Lookahead != 0), "強 uses the 2-ply read");
            var pw = new CpuProfile(CpuStrength.Weak, 0).Personality(FormationStyle.Balanced);
            var pn = new CpuProfile(CpuStrength.Normal, 0).Personality(FormationStyle.Balanced);
            var ps = new CpuProfile(CpuStrength.Strong, 0).Personality(FormationStyle.Balanced);
            Check(pw.NearMargin > pn.NearMargin && pn.NearMargin > ps.NearMargin, "choice margin narrows with strength");
            Check(ps.InformationWeight > pn.InformationWeight, "強 values information more");

            // The weak CPU still plays with purpose: it beats a random mover.
            int wins = 0;
            for (int g = 0; g < 6; g++)
            {
                var cpuSide = g % 2 == 0 ? Side.North : Side.South;
                var cpu = new CpuPlayer(cpuSide, 40 + g, 50 + g, new CpuProfile(CpuStrength.Weak, 0));
                var other = FormationGenerator.Generate(cpuSide.Opponent(), FormationStyle.Balanced, 60 + g);
                var match = cpuSide == Side.North ? new Match(other, cpu.CreateFormation()) : new Match(cpu.CreateFormation(), other);
                var rng = new DeterministicRandom(70 + g);
                while (match.Status == GameStatus.Playing)
                {
                    if (match.ToMove == cpuSide) match.Apply(cpuSide, cpu.Decide(match.GetView(cpuSide)).Chosen.Command);
                    else { var ms = match.LegalMoves(match.ToMove); match.Apply(match.ToMove, ms[rng.Next(ms.Count)]); }
                }
                if (match.Winner == cpuSide) wins++;
            }
            Check(wins >= 5, "弱 still beats a random mover (" + wins + "/6)");
            Metrics.Add("弱 vs random mover: " + wins + "/6");
        }

        /// <summary>Strength and temperament never touch what the CPU knows.</summary>
        public static void TemperamentKeepsKnowledge()
        {
            var driver = new CpuPlayer(Side.South, 5, 6);
            var north = new CpuPlayer(Side.North, 7, 8);
            var match = new Match(driver.CreateFormation(), north.CreateFormation());
            var observers = new List<CpuPlayer>();
            foreach (var strength in new[] { CpuStrength.Weak, CpuStrength.Normal, CpuStrength.Strong })
                for (int t = -2; t <= 2; t++) observers.Add(new CpuPlayer(Side.North, 7, 8, new CpuProfile(strength, t)));
            while (match.Status == GameStatus.Playing && match.Ply < 70)
            {
                var side = match.ToMove;
                var cmd = side == Side.South ? driver.Decide(match.GetView(side)).Chosen.Command : north.Decide(match.GetView(side)).Chosen.Command;
                match.Apply(side, cmd);
                var view = match.GetView(Side.North);
                string reference = null;
                foreach (var o in observers)
                {
                    if (match.ToMove == Side.North && match.Status == GameStatus.Playing) o.Decide(view); else o.Observe(view);
                    string k = string.Join("|", o.Knowledge.Enemies.Select(b => string.Join(",", b.Probability.Select(p => p.ToString("R"))) + ":" + string.Concat(b.Allowed.Select(a => a ? '1' : '0')) + ":" + b.Notes.Count));
                    if (reference == null) reference = k; else Check(k == reference, "knowledge differs between CPU profiles at ply " + match.Ply);
                }
            }
        }

        public static void TemperamentShapesStyleAndPlay()
        {
            var counts = new int[5, 5];
            for (int t = -2; t <= 2; t++)
                for (int seed = 1; seed <= 2000; seed++)
                {
                    var s = CpuProfile.ChooseStyle(seed, t);
                    Check(s == CpuProfile.ChooseStyle(seed, t), "style choice deterministic");
                    counts[t + 2, (int)s]++;
                }
            int Count(int t, FormationStyle s) => counts[t + 2, (int)s];
            Check(Count(-2, FormationStyle.Aggressive) == 0 && Count(-2, FormationStyle.Mobile) == 0, "かなり防御的 never gets an attack formation");
            Check(Count(2, FormationStyle.Defensive) == 0 && Count(2, FormationStyle.Trap) == 0, "かなり攻撃的 never gets a defensive formation");
            Check(Count(-1, FormationStyle.Aggressive) == 0 && Count(1, FormationStyle.Defensive) == 0, "攻撃的/防御的 avoid the opposite extreme");
            Check(Enum.GetValues(typeof(FormationStyle)).Cast<FormationStyle>().All(s => Count(0, s) > 100), "バランス uses every style");
            Check(Enum.GetValues(typeof(FormationStyle)).Cast<FormationStyle>().All(s => Count(0, FormationStyle.Balanced) >= Count(0, s)), "バランス favours じっくりいこうぜ");
            Metrics.Add("style by temperament (Def/Agg/Bal/Trap/Mob per 2000): " + string.Join(" | ",
                Enumerable.Range(-2, 5).Select(t => t + ":" + string.Join("/", Enumerable.Range(0, 5).Select(i => counts[t + 2, i])))));

            // Evaluation weights move monotonically with temperament for the same formation style.
            for (int t = -2; t < 2; t++)
            {
                var a = new CpuProfile(CpuStrength.Normal, t).Personality(FormationStyle.Balanced);
                var b = new CpuProfile(CpuStrength.Normal, t + 1).Personality(FormationStyle.Balanced);
                Check(b.ProgressWeight > a.ProgressWeight && b.OpportunityWeight > a.OpportunityWeight, "more aggressive: more progress/opportunity");
                Check(b.DefenseWeight < a.DefenseWeight && b.RiskAversion < a.RiskAversion, "more aggressive: less defence/risk aversion");
            }

            // In play: same seeds, the aggressive CPU attacks more and advances officers further.
            double Attacks(int t, out double depth)
            {
                int attacks = 0, decisions = 0; double adv = 0;
                for (int g = 0; g < 8; g++)
                {
                    var cpu = new CpuPlayer(Side.North, 300 + g, 400 + g, new CpuProfile(CpuStrength.Normal, t));
                    var match = new Match(FormationGenerator.Generate(Side.South, FormationStyle.Balanced, 500 + g), cpu.CreateFormation());
                    var rng = new DeterministicRandom(600 + g);
                    while (match.Status == GameStatus.Playing && match.Ply < 120)
                    {
                        if (match.ToMove == Side.North)
                        {
                            var r = cpu.Decide(match.GetView(Side.North));
                            decisions++;
                            if (r.Chosen.TargetEnemyId >= 0) attacks++;
                            var v = match.GetView(Side.North);
                            adv += v.Own.Where(p => p.Alive && PieceCatalog.CanCaptureHeadquarters(p.Type)).Select(p => (double)BoardGraph.DistanceToHeadquarters(p.Node, Side.South)).DefaultIfEmpty(10).Average();
                            match.Apply(Side.North, r.Chosen.Command);
                        }
                        else match.Apply(Side.South, GameTests.GreedyMove(match, rng));
                    }
                }
                depth = adv / Math.Max(1, decisions);
                return attacks / (double)Math.Max(1, decisions);
            }
            double aggressive = Attacks(2, out double aggDepth), defensive = Attacks(-2, out double defDepth);
            Metrics.Add($"temperament in play: attack rate かなり攻撃的 {aggressive:P1} vs かなり防御的 {defensive:P1}; mean officer distance to enemy HQ {aggDepth:0.00} vs {defDepth:0.00}");
            Check(aggressive > defensive, "aggressive temperament attacks more often");
            Check(aggDepth < defDepth, "aggressive temperament keeps officers closer to the enemy HQ");
        }

        /// <summary>Weak/strong CPUs also produce identical decisions when only hidden enemy kinds differ.</summary>
        public static void ProfilesStayBlind()
        {
            foreach (var strength in new[] { CpuStrength.Weak, CpuStrength.Strong })
                foreach (int t in new[] { -2, 2 })
                    BoundaryTests.HiddenKindsDoNotChangeDecision(() => new CpuPlayer(Side.North, 5, 77, new CpuProfile(strength, t), FormationStyle.Aggressive));
        }

        /// <summary>「あそびかた」 combat chart = the referee's Judge, including the flag rule.</summary>
        public static void HelpCombatMatchesJudge()
        {
            var state = new AuthoritativeGameState(FormationGenerator.Generate(Side.South, FormationStyle.Balanced, 1), FormationGenerator.Generate(Side.North, FormationStyle.Balanced, 2));
            foreach (var a in PieceCatalog.AllTypes.Where(PieceCatalog.IsMobile))
                foreach (var d in PieceCatalog.AllTypes.Where(d => d != PieceType.Flag))
                {
                    var outcome = Judge.Resolve(state, new PieceState { Type = a, Side = Side.South, Node = -1 }, new PieceState { Type = d, Side = Side.North, Node = -1 });
                    var help = RuleReference.Against(a, d);
                    var expected = outcome == CombatOutcome.AttackerWins ? Relation.Wins : outcome == CombatOutcome.DefenderWins ? Relation.Loses : Relation.Ties;
                    Check(help == expected, $"help {a} vs {d}: {help}, judge {outcome}");
                    // Symmetric reading: the defender's chart says the opposite.
                    var reverse = RuleReference.Against(d, a);
                    var expectedReverse = expected == Relation.Wins ? Relation.Loses : expected == Relation.Loses ? Relation.Wins : Relation.Ties;
                    Check(reverse == expectedReverse, $"help {d} vs {a} (defending)");
                }
            // Flag: fights as the piece behind it; alone it loses to everything.
            foreach (var backer in PieceCatalog.AllTypes.Where(t => t != PieceType.Flag))
                foreach (var a in PieceCatalog.AllTypes.Where(PieceCatalog.IsMobile))
                {
                    var flagNode = BoardGraph.Cell(3, 6);
                    var backNode = FlagRule.BackingCell(flagNode, Side.North);
                    for (int i = 0; i < state.PieceAt.Length; i++) state.PieceAt[i] = -1;
                    var pieces = state.Pieces;
                    pieces[40].Type = backer; pieces[40].Side = Side.North; pieces[40].Node = backNode; state.PieceAt[backNode] = 40;
                    var flag = new PieceState { Type = PieceType.Flag, Side = Side.North, Node = flagNode };
                    var withBacker = Judge.Resolve(state, new PieceState { Type = a, Side = Side.South }, flag);
                    Check(withBacker == CombatTable.Resolve(a, backer), "flag fights as its backer");
                    state.PieceAt[backNode] = -1;
                    Check(Judge.Resolve(state, new PieceState { Type = a, Side = Side.South }, flag) == CombatOutcome.AttackerWins, "lone flag loses");
                }
            Check(RuleReference.Opponents(PieceType.Flag, Relation.DependsOnBacker).Count == 16, "flag entry refers to its backer");
            Check(RuleReference.Opponents(PieceType.Engineer, Relation.Wins).SequenceEqual(new[] { PieceType.Tank, PieceType.Spy, PieceType.Mine }), "工兵 wins list");
        }

        /// <summary>「あそびかた」 movement diagrams are the real move generator on real board positions.</summary>
        public static void HelpMovementMatchesRules()
        {
            foreach (MoveClass cls in Enum.GetValues(typeof(MoveClass)))
            {
                var ex = RuleReference.Example(cls);
                var direct = new List<MoveTarget>();
                MoveRules.Generate(cls, Side.South, ex.From, ex.Owners, direct);
                Check(direct.Select(t => t.To).SequenceEqual(ex.Targets.Select(t => t.To)), cls + " example equals the move generator");
                var to = new HashSet<int>(ex.Targets.Select(t => t.To));
                switch (cls)
                {
                    case MoveClass.Step:
                        Check(to.Count == 4 && to.Contains(BoardGraph.LeftCrossing), "step: 4 destinations incl. the centre circle");
                        break;
                    case MoveClass.Charger:
                        Check(ex.Targets.Any(t => t.Path.Length == 2 && t.To == BoardGraph.LeftCrossing), "charger: 2 forward ending in the centre circle");
                        Check(!to.Contains(BoardGraph.Cell(0, 4)) && !to.Contains(BoardGraph.Cell(2, 4)), "charger: stops in the circle");
                        break;
                    case MoveClass.Slider:
                        Check(to.Contains(BoardGraph.Cell(3, 1)) && !to.Contains(BoardGraph.Cell(4, 1)) && !to.Contains(BoardGraph.Cell(5, 1)), "slider: blocked, no jump");
                        Check(to.Contains(BoardGraph.LeftCrossing) && !to.Contains(BoardGraph.Cell(0, 4)), "slider: stops in the circle");
                        break;
                    case MoveClass.Flyer:
                        Check(ex.Targets.Any(t => t.Jumped == 2 && t.To == BoardGraph.Cell(4, 4)), "flyer: jumps two pieces across the band");
                        Check(ex.Targets.Any(t => t.IsAttack && t.To == BoardGraph.Cell(4, 5)) && to.Contains(BoardGraph.Cell(4, 7)), "flyer: attacks and flies past");
                        Check(to.Contains(BoardGraph.Cell(3, 1)) && to.Contains(BoardGraph.Cell(5, 1)) && !to.Contains(BoardGraph.Cell(2, 1)), "flyer: one step sideways");
                        Check(!to.Any(BoardGraph.IsCrossing), "flyer: never the centre circle");
                        break;
                    case MoveClass.Immobile:
                        Check(to.Count == 0, "immobile");
                        break;
                }
            }
            foreach (var t in PieceCatalog.AllTypes)
                Check(RuleReference.MoveSummary(t) == RuleReference.MoveSummary(PieceCatalog.AllTypes.First(o => PieceCatalog.MoveClassOf(o) == PieceCatalog.MoveClassOf(t))), "summary follows move class");
            var cfg = new MatchConfig();
            Check(cfg.MaxPlies == 800 && cfg.MaxPliesWithoutCombat == 160, "help draw rule numbers match MatchConfig");
        }
    }
}
