using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace MilitaryShogi.Tests
{
    /// <summary>Minimal self-contained test runner (no external packages). Exit code 0 = all passed.</summary>
    internal static class Program
    {
        private static int checks;
        private static readonly List<string> failures = new List<string>();
        public static readonly List<string> Metrics = new List<string>();

        public static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception(message);
        }

        private static int Main(string[] args)
        {
            bool quick = args.Contains("--quick");
            if (args.Contains("--diag")) { Diagnostics.Run(); return 0; }
            if (args.Length == 2 && args[0] == "--trace") { Diagnostics.Trace(int.Parse(args[1])); return 0; }
            // --legacy-draw: games end only by the pre-1.3.0 rules, so CPU metrics compare with 1.1.1/1.2.x.
            if (args.Contains("--legacy-draw")) MilitaryShogi.Engine.MatchConfig.DefaultDrawWhenNoCapturers = false;
            var suites = new List<(string, Action)>
            {
                ("Rules: army composition", RulesTests.ArmyComposition),
                ("Rules: combat table vs. textual rules", RulesTests.CombatTableMatchesRules),
                ("Rules: board graph", RulesTests.BoardGraphStructure),
                ("Rules: movement per class", RulesTests.MovementScenarios),
                ("Rules: formation constraints & seed reproducibility", RulesTests.Formations),
                ("Rules: manual placement edits", RulesTests.ManualEdits),
                ("Engine: flag / mine / airplane combat through the judge", EngineTests.JudgeScenarios),
                ("Engine: headquarters capture only by 大将〜少佐", EngineTests.HeadquartersRule),
                ("Engine: legal-move invariants over random games", EngineTests.RandomGameInvariants),
                ("Boundary: CPU assembly cannot reach the engine", BoundaryTests.AssemblyReferences),
                ("Boundary: observation types carry no enemy kinds", BoundaryTests.ObservationTypes),
                ("Boundary: CPU source scan", BoundaryTests.SourceScan),
                ("Boundary: decisions independent of hidden enemy kinds", BoundaryTests.HiddenKindsDoNotChangeDecision),
                ("CPU: knowledge updates from observations", CpuTests.KnowledgeScenarios),
                ("CPU: formation style & seed", CpuTests.FormationStyles),
                ("CPU: decision seed reproducibility", CpuTests.DecisionSeedReproducibility),
                ("1.1.0 CPU: 中・バランス reproduces 1.0.0", ProfileTests.StandardEqualsLegacy),
                ("1.1.0 CPU: 弱/中/強 lookahead and choice precision", ProfileTests.StrengthLevels),
                ("1.1.0 CPU: strength/temperament do not change knowledge", ProfileTests.TemperamentKeepsKnowledge),
                ("1.1.0 CPU: temperament shapes formation style and play", ProfileTests.TemperamentShapesStyleAndPlay),
                ("1.1.0 Boundary: 弱/強 and every temperament stay blind to hidden kinds", ProfileTests.ProfilesStayBlind),
                ("1.1.0 Help: combat chart equals the Judge", ProfileTests.HelpCombatMatchesJudge),
                ("1.1.0 Help: movement diagrams equal the move generator", ProfileTests.HelpMovementMatchesRules),
                ("1.1.1 Player facts: movement, jump, removal", PlayerFactsTests.MovementAndRemoval),
                ("1.1.1 Player facts: combat and ambiguity", PlayerFactsTests.CombatAndAmbiguity),
                ("1.1.1 Player facts: information boundary and soundness", PlayerFactsTests.BoundaryAndSoundness),
                ("1.2.0 Presets: five slots, names, placement round trip", PresetTests.SlotsNamesAndRoundTrip),
                ("1.2.0 Presets: invalid data is rejected", PresetTests.RejectsInvalidData),
                ("1.3.0 Engine: draw when neither side can capture the headquarters", EngineTests.NoCapturersDraw),
                ("1.3.0 Engine: resignation", EngineTests.Resignation),
                ("1.4.0 Referee: no intervention without a 5-fold public position", RefereeTests.NoInterventionWithoutRepetition),
                ("1.4.0 Referee: intervenes on the 5th occurrence, once per repetition", RefereeTests.InterventionOnFifthOccurrenceOnce),
                ("1.4.0 Referee: hidden kinds do not change when or toward whom", RefereeTests.HiddenKindsDoNotMatter),
                ("1.4.0 Referee: the threatened side is addressed", RefereeTests.DefenderIsTheThreatenedSide),
                ("1.4.0 Referee: CPU plays its next-best move, evaluation untouched", RefereeTests.CpuPlaysNextBestAfterIntervention),
                ("Games: CPU vs CPU and CPU vs random complete; beliefs stay sound", () => GameTests.FullGames(quick)),
            };

            // --only TEXT: run just the suites whose name contains TEXT (development; the full run has no filter).
            int only = Array.IndexOf(args, "--only");
            if (only >= 0 && only + 1 < args.Length) suites = suites.Where(s => s.Item1.Contains(args[only + 1])).ToList();
            var total = Stopwatch.StartNew();
            foreach (var (name, run) in suites)
            {
                var sw = Stopwatch.StartNew();
                int before = checks;
                try
                {
                    run();
                    Console.WriteLine($"PASS  {name}  ({checks - before} checks, {sw.ElapsedMilliseconds} ms)");
                }
                catch (Exception e)
                {
                    failures.Add(name);
                    Console.WriteLine($"FAIL  {name}: {e.Message}");
                    Console.WriteLine(e.StackTrace);
                }
            }
            Console.WriteLine();
            foreach (var m in Metrics) Console.WriteLine("METRIC " + m);
            Console.WriteLine($"{checks} checks, {failures.Count} failed suites, {total.Elapsed.TotalSeconds:0.0} s");
            return failures.Count == 0 ? 0 : 1;
        }
    }
}
