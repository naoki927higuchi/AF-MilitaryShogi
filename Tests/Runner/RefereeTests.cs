using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MilitaryShogi.Cpu;
using MilitaryShogi.Engine;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;
using static MilitaryShogi.Tests.Program;

namespace MilitaryShogi.Tests
{
    /// <summary>1.4.0 stalemate referee (RepetitionReferee) and the CPU's next-best choice after it.</summary>
    internal static class RefereeTests
    {
        // South shuffles its piece at (1,1) into the empty (0,1) and back; North does the same at (1,6)/(0,6).
        private static Match Oscillation(PieceType northShuffler)
        {
            var south = Scenario.Build(Side.South, new Dictionary<(int, int), PieceType> { { (1, 1), PieceType.SecondLieutenant } }, 0, 1);
            var north = Scenario.Build(Side.North, new Dictionary<(int, int), PieceType> { { (1, 6), northShuffler } }, 0, 6);
            return new Match(south, north);
        }

        private static readonly (Side side, int from, int to)[] Cycle =
        {
            (Side.South, BoardGraph.Cell(1, 1), BoardGraph.Cell(0, 1)),
            (Side.North, BoardGraph.Cell(1, 6), BoardGraph.Cell(0, 6)),
            (Side.South, BoardGraph.Cell(0, 1), BoardGraph.Cell(1, 1)),
            (Side.North, BoardGraph.Cell(0, 6), BoardGraph.Cell(1, 6)),
        };

        private static void Play(Match m, RepetitionReferee r, (Side side, int from, int to) step, List<string> log)
        {
            int id = Scenario.IdAt(m.GetView(step.side), step.from);
            m.Apply(step.side, new MoveCommand(id, step.to));
            var iv = r.Record(m.GetView(Side.South));
            if (iv != null) log.Add(iv.ToString());
        }

        /// <summary>1. Many ordinary moves without a repeated position: no intervention.</summary>
        public static void NoInterventionWithoutRepetition()
        {
            int games = 0, plies = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                var rng = new DeterministicRandom(seed * 17);
                var m = new Match(FormationGenerator.Generate(Side.South, FormationStyle.Balanced, seed), FormationGenerator.Generate(Side.North, FormationStyle.Mobile, seed));
                var r = new RepetitionReferee();
                r.Start(m.GetView(Side.South));
                var seen = new Dictionary<string, int> { { RepetitionReferee.Key(m.GetView(Side.South)), 1 } };
                while (m.Status == GameStatus.Playing && m.Ply < 120)
                {
                    var legal = m.LegalMoves(m.ToMove);
                    m.Apply(m.ToMove, legal[rng.Next(legal.Count)]);
                    string key = RepetitionReferee.Key(m.GetView(Side.South));
                    seen[key] = seen.TryGetValue(key, out int c) ? c + 1 : 1;
                    var iv = r.Record(m.GetView(Side.South));
                    // The referee may only speak when some public position has really come back 5 times.
                    Check(iv == null || seen[key] >= RepetitionReferee.Occurrences, "intervention without a 5-fold position");
                    if (seen.Values.All(v => v < RepetitionReferee.Occurrences)) Check(iv == null, "no intervention while no position repeats 5 times");
                    plies++;
                }
                games++;
            }
            Metrics.Add("1.4.0 referee: " + games + " random games / " + plies + " plies checked, interventions only on 5-fold public positions");
        }

        /// <summary>2 + 4. A 4-ply back-and-forth: intervention exactly when the position appears the 5th time, and only once.</summary>
        public static void InterventionOnFifthOccurrenceOnce()
        {
            var m = Oscillation(PieceType.Colonel);
            var r = new RepetitionReferee();
            r.Start(m.GetView(Side.South));
            var log = new List<string>();
            int firstAt = -1;
            for (int ply = 0; ply < 60; ply++)
            {
                int before = log.Count;
                Play(m, r, Cycle[ply % 4], log);
                if (log.Count > before && firstAt < 0) firstAt = m.Ply;
            }
            Check(firstAt == 16, "intervenes on the 5th occurrence of the start position (ply 16), got " + firstAt);
            Check(log.Count == 1, "only one intervention for the same repetition (60 plies), got " + log.Count);
            var iv = r.Interventions[0];
            Check(iv.Occurrences == 5 && iv.CyclePlies == 4, "5 occurrences, 4-ply cycle: " + iv);
            // Both shuffle in their own camps, equally far from the other headquarters → both addressed.
            Check(iv.Addresses(Side.South) && iv.Addresses(Side.North), "symmetric shuffle addresses both sides");
            Check(m.Status == GameStatus.Playing, "the referee does not end or change the game");
        }

        /// <summary>3. Different hidden kinds, same public positions → same interventions at the same plies.</summary>
        public static void HiddenKindsDoNotMatter()
        {
            string Run(PieceType shuffler)
            {
                var m = Oscillation(shuffler);
                var r = new RepetitionReferee();
                r.Start(m.GetView(Side.South));
                var trace = new List<string>();
                for (int ply = 0; ply < 40; ply++)
                {
                    int id = Scenario.IdAt(m.GetView(Cycle[ply % 4].side), Cycle[ply % 4].from);
                    m.Apply(Cycle[ply % 4].side, new MoveCommand(id, Cycle[ply % 4].to));
                    var iv = r.Record(m.GetView(Side.South));
                    trace.Add(RepetitionReferee.Key(m.GetView(Side.South)) + (iv == null ? "" : " !" + iv));
                }
                return string.Join("\n", trace);
            }
            // 大佐 can capture the headquarters, 少尉 cannot; both move one step, so the public game is identical.
            string capturer = Run(PieceType.Colonel), bluff = Run(PieceType.SecondLieutenant);
            Check(capturer == bluff, "referee output identical whether the enemy shuffler can capture the HQ or not");
            Check(capturer.Contains("!"), "the scenario does intervene");
            // Structural: the referee only sees ids, nodes, side to move and public moves.
            string src = File.ReadAllText(Path.Combine(ProjectRoot(), "Assets", "Scripts", "Observation", "RepetitionReferee.cs"));
            foreach (var banned in new[] { ".Type", "PieceType", "Probability", "Belief", "Reveal", "CanCaptureHeadquarters", "Engine" })
                Check(!src.Replace("MilitaryShogi.Engine", "").Contains(banned), "RepetitionReferee must not read " + banned);
        }

        /// <summary>The side whose headquarters the opponent's cycling pieces are near is addressed.</summary>
        public static void DefenderIsTheThreatenedSide()
        {
            var nearSouthHq = new Dictionary<Side, HashSet<int>>
            {
                { Side.North, new HashSet<int> { BoardGraph.Cell(3, 1), BoardGraph.Cell(2, 1) } },   // attacker shuffles in front of South's HQ
                { Side.South, new HashSet<int> { BoardGraph.Cell(3, 0), BoardGraph.Cell(2, 0) } },   // defender mirrors
            };
            var t = RepetitionReferee.Defenders(nearSouthHq);
            Check(t.Length == 1 && t[0] == Side.South, "defender (South) is addressed, not the attacker");
            var nearNorthHq = new Dictionary<Side, HashSet<int>>
            {
                { Side.South, new HashSet<int> { BoardGraph.Cell(4, 6), BoardGraph.Cell(5, 6) } },
                { Side.North, new HashSet<int> { BoardGraph.Cell(4, 7), BoardGraph.Cell(5, 7) } },
            };
            t = RepetitionReferee.Defenders(nearNorthHq);
            Check(t.Length == 1 && t[0] == Side.North, "mirror case: North is addressed");
        }

        /// <summary>5 + 7. After an intervention toward the CPU, it plays its next-best move that leaves the repetition; scores unchanged.</summary>
        public static void CpuPlaysNextBestAfterIntervention()
        {
            var m = Oscillation(PieceType.Colonel);
            var r = new RepetitionReferee();
            r.Start(m.GetView(Side.South));
            var log = new List<string>();
            for (int ply = 0; ply < 16; ply++) Play(m, r, Cycle[ply % 4], log);
            Check(r.IsRestricted(Side.North), "North (CPU) addressed");
            Play(m, r, Cycle[0], log);                        // South shuffles again; North to move in the cycle
            var view = m.GetView(Side.North);
            var cpu = new CpuPlayer(Side.North, 6, 7);
            cpu.Observe(view);
            var report = cpu.Decide(view);
            var scores = report.Candidates.Select(c => c.Command + "=" + c.Score.ToString("R")).ToList();
            Func<CandidateMove, bool> continues = c => r.ContinuesRepetition(Side.North, view, c.Command, view.Owners[c.Command.To] == (sbyte)Side.South);
            var cycleMove = report.Candidates.First(c => c.Command.To == BoardGraph.Cell(0, 6) && c.From == BoardGraph.Cell(1, 6));
            Check(continues(cycleMove), "the shuffle move continues the repetition");
            // Force the case "the CPU chose the shuffle" to check the rule itself.
            report.Chosen = cycleMove;
            bool changed = RefereeChoice.Apply(report, continues);
            Check(changed && !continues(report.Chosen), "the CPU plays a move that leaves the repetition");
            var expected = report.Candidates.First(c => !continues(c));
            Check(report.Chosen == expected, "it is the best-scored move that leaves the repetition (next best)");
            Check(report.Reason.StartsWith(RefereeChoice.NotePrefix), "the report says why");
            Check(report.Candidates.Select(c => c.Command + "=" + c.Score.ToString("R")).SequenceEqual(scores), "evaluation untouched: same candidates and scores");
            // Its own untouched choice is kept when it does not continue the repetition.
            var fresh = cpu.Decide(view);
            var own = fresh.Chosen;
            if (!continues(own)) Check(!RefereeChoice.Apply(fresh, continues) && fresh.Chosen == own, "a non-repeating choice is left alone");
            // A side that was not addressed is never restricted.
            var r2 = new RepetitionReferee();
            Check(!r2.IsRestricted(Side.North) && !r2.ContinuesRepetition(Side.North, view, cycleMove.Command, false), "no restriction without an intervention");
        }

        private static string ProjectRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "VERSION.txt"))) d = d.Parent;
            return d.FullName;
        }
    }
}
