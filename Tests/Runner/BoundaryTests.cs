using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MilitaryShogi.Cpu;
using MilitaryShogi.Engine;
using Match = MilitaryShogi.Engine.Match;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;
using static MilitaryShogi.Tests.Program;

namespace MilitaryShogi.Tests
{
    /// <summary>
    /// Checks that the CPU cannot see the true kinds of enemy pieces.
    /// Layers: (1) assembly references, (2) the observation data contract,
    /// (3) source scan for escape hatches, (4) behaviour: swapping hidden kinds
    /// does not change the CPU's decision.
    /// </summary>
    internal static class BoundaryTests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets"))) dir = dir.Parent;
            if (dir == null) throw new Exception("project root not found");
            return dir.FullName;
        }

        public static void AssemblyReferences()
        {
            var cpu = typeof(CpuPlayer).Assembly;
            var refs = cpu.GetReferencedAssemblies().Select(a => a.Name).ToList();
            Check(!refs.Contains(typeof(Match).Assembly.GetName().Name), "MilitaryShogi.Cpu must not reference MilitaryShogi.Engine");
            Check(refs.Contains("MilitaryShogi.Observation") && refs.Contains("MilitaryShogi.Rules"), "CPU uses Observation + Rules");

            // Unity asmdef must say the same.
            string asmdef = File.ReadAllText(Path.Combine(Root(), "Assets", "Scripts", "Cpu", "MilitaryShogi.Cpu.asmdef"));
            var m = Regex.Match(asmdef, "\"references\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            var names = Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"").Select(x => x.Groups[1].Value).OrderBy(x => x).ToArray();
            Check(names.SequenceEqual(new[] { "MilitaryShogi.Observation", "MilitaryShogi.Rules" }), "Cpu.asmdef references: " + string.Join(",", names));
            string obs = File.ReadAllText(Path.Combine(Root(), "Assets", "Scripts", "Observation", "MilitaryShogi.Observation.asmdef"));
            Check(!obs.Contains("MilitaryShogi.Engine"), "Observation must not reference Engine");

            // The authoritative state and the judge are not public.
            var engine = typeof(Match).Assembly;
            foreach (var name in new[] { "AuthoritativeGameState", "PieceState", "Judge", "ObservationBuilder" })
            {
                var t = engine.GetTypes().First(x => x.Name == name);
                Check(!t.IsPublic, name + " is internal");
            }
            // Match exposes no member returning PieceType other than the post-game reveal.
            foreach (var member in typeof(Match).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                Type rt = member is MethodInfo mi ? mi.ReturnType : member is PropertyInfo pi ? pi.PropertyType : member is FieldInfo fi ? fi.FieldType : null;
                if (rt == typeof(PieceType)) Check(member.Name == "RevealAfterGameEnd", "unexpected PieceType accessor on Match: " + member.Name);
            }
        }

        public static void ObservationTypes()
        {
            // Nothing that describes an enemy piece or public history may carry a PieceType.
            foreach (var t in new[] { typeof(EnemyPieceView), typeof(ObservedMove), typeof(ObservedCombat), typeof(MoveCommand) })
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    Check(f.FieldType != typeof(PieceType) && f.FieldType != typeof(PieceType?), t.Name + "." + f.Name + " exposes a kind");
            // PlayerView's only kind-bearing data is the list of own pieces.
            foreach (var f in typeof(PlayerView).GetFields(BindingFlags.Public | BindingFlags.Instance))
                Check(f.FieldType != typeof(PieceType), "PlayerView." + f.Name);
            var ownField = typeof(OwnPieceView).GetField("Type");
            Check(ownField != null, "own pieces carry their kind");
        }

        public static void SourceScan()
        {
            var dir = Path.Combine(Root(), "Assets", "Scripts", "Cpu");
            var forbidden = new[] { "MilitaryShogi.Engine", "AuthoritativeGameState", "RevealAfterGameEnd", "System.Reflection", "GetField(", "GetType(", "Activator", "dynamic ", "unsafe" };
            int files = 0;
            foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                files++;
                var text = File.ReadAllText(file);
                foreach (var f in forbidden) Check(!text.Contains(f), Path.GetFileName(file) + " contains '" + f + "'");
            }
            Check(files >= 4, "scanned CPU sources");
        }

        /// <summary>
        /// Two south armies with identical occupancy but different hidden kinds, playing the same
        /// first move: the CPU (north) must produce byte-identical decisions, because nothing it
        /// can observe differs.
        /// </summary>
        public static void HiddenKindsDoNotChangeDecision() { HiddenKindsDoNotChangeDecision(null); }

        public static void HiddenKindsDoNotChangeDecision(Func<CpuPlayer> makeCpu)
        {
            var baseSouth = FormationGenerator.Generate(Side.South, FormationStyle.Balanced, 99);
            var north = FormationGenerator.Generate(Side.North, FormationStyle.Aggressive, 5);
            // Shuffle the kinds among cells where the placement is still legal.
            var rng = new DeterministicRandom(1234);
            var cells = baseSouth.Pieces.Keys.OrderBy(k => k).ToList();
            Formation shuffled = null;
            for (int attempt = 0; attempt < 200 && shuffled == null; attempt++)
            {
                var kinds = cells.Select(c => baseSouth.Pieces[c]).ToList();
                rng.Shuffle(kinds);
                var f = new Formation(Side.South, cells.Zip(kinds, (c, k) => (c, k)).ToDictionary(x => x.c, x => x.k));
                if (PlacementRules.IsValid(f) && f.Signature() != baseSouth.Signature()) shuffled = f;
            }
            Check(shuffled != null, "found a legal shuffled army");

            string Decision(Formation south)
            {
                var match = new Match(south, north);
                // Same first south move in both worlds: a piece that is mobile in both armies.
                var sv = match.GetView(Side.South);
                MoveCommand first = default;
                bool found = false;
                foreach (var c in match.LegalMoves(Side.South))
                {
                    var node = sv.OwnById(c.PieceId).Node;
                    if (PieceCatalog.IsMobile(baseSouth.Pieces[node]) && PieceCatalog.IsMobile(shuffled.Pieces[node])
                        && PieceCatalog.MoveClassOf(baseSouth.Pieces[node]) == MoveClass.Step && PieceCatalog.MoveClassOf(shuffled.Pieces[node]) == MoveClass.Step
                        && sv.Owners[c.To] == MoveRules.Empty)
                    { first = c; found = true; break; }
                }
                Check(found, "common first move");
                match.Apply(Side.South, first);
                var cpu = makeCpu != null ? makeCpu() : new CpuPlayer(Side.North, 5, 77, FormationStyle.Aggressive);
                var report = cpu.Decide(match.GetView(Side.North));
                return report.Chosen.Command + "|" + string.Join(";", report.Candidates.Select(c => c.Command + "=" + c.Score.ToString("R")))
                    + "|" + string.Join(";", report.Beliefs.Select(b => string.Join(",", b.Probability.Select(p => p.ToString("R")))));
            }

            Check(Decision(baseSouth) == Decision(shuffled), "CPU decision depends only on observations");
        }
    }
}
