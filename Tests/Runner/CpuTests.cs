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
    internal static class CpuTests
    {
        private static int C(int x, int y) => BoardGraph.Cell(x, y);

        private static void CheckCounts(CpuKnowledge k)
        {
            foreach (var t in PieceCatalog.AllTypes)
            {
                double s = k.Enemies.Sum(b => b.P(t));
                Check(Math.Abs(s - PieceCatalog.Count(t)) < 1e-3, $"column sum {t} = {s}");
            }
            foreach (var b in k.Enemies)
            {
                Check(Math.Abs(b.Probability.Sum() - 1) < 1e-9, "row sums to 1");
                for (int t = 0; t < 16; t++) if (!b.Allowed[t]) Check(b.Probability[t] == 0, "excluded kind has zero probability");
            }
        }

        public static void KnowledgeScenarios()
        {
            // --- initial beliefs ---
            var south = new Dictionary<(int, int), PieceType> { [(1, 0)] = PieceType.Airplane, [(4, 1)] = PieceType.Captain };
            var north = new Dictionary<(int, int), PieceType> { [(3, 4)] = PieceType.Airplane, [(6, 4)] = PieceType.MajorGeneral };
            var m = new Match(Scenario.Build(Side.South, south, 1, 2), Scenario.Build(Side.North, north, 6, 6));
            var cpu = new CpuPlayer(Side.North, 1, 1, FormationStyle.Balanced);
            cpu.Observe(m.GetView(Side.North));
            var k = cpu.Knowledge;
            CheckCounts(k);
            var armPiece = k.Belief(Scenario.IdAt(m.GetView(Side.North), C(0, 3)));
            Check(!armPiece.Allowed[(int)PieceType.Mine] && !armPiece.Allowed[(int)PieceType.Flag], "arm-end piece cannot be mine/flag");
            var backPiece = k.Belief(Scenario.IdAt(m.GetView(Side.North), C(5, 0)));
            Check(!backPiece.Allowed[(int)PieceType.Flag] && backPiece.Allowed[(int)PieceType.Mine], "back-row piece cannot be flag");

            // --- ply 1: south airplane (1,0) jumps over (1,1) to (1,2) ---
            int airId = Scenario.IdAt(m.GetView(Side.South), C(1, 0));
            m.Apply(Side.South, new MoveCommand(airId, C(1, 2)));
            cpu.Observe(m.GetView(Side.North));
            var air = k.Belief(airId);
            Check(air.Candidates().SequenceEqual(new[] { PieceType.Airplane }), "jump -> airplane confirmed: " + string.Join(",", air.Candidates()));
            Check(Math.Abs(air.P(PieceType.Airplane) - 1) < 1e-6, "P(airplane)=1");
            Check(air.Notes.Any(n => n.Kind == NoteKind.Excluded && n.Text.Contains("飛び越え")), "reason mentions the jump");
            Check(air.Notes.Any(n => n.Text.Contains("地雷") && n.Text.Contains("除外")), "reason mentions mine excluded");
            CheckCounts(k);
            double otherAir = k.Enemies.Where(b => b.Id != airId).Sum(b => b.P(PieceType.Airplane));
            Check(Math.Abs(otherAir - 1) < 1e-3, "the other 30 pieces share exactly one airplane");

            // --- ply 2: north airplane (3,4) attacks south (3,1); the outcome constrains that piece ---
            var nv = m.GetView(Side.North);
            int target = Scenario.IdAt(nv, C(3, 1));
            var r = m.Apply(Side.North, new MoveCommand(Scenario.IdAt(nv, C(3, 4)), C(3, 1)));
            cpu.Observe(m.GetView(Side.North));
            var tb = k.Belief(target);
            var outcome = r.Combat.Outcome;
            foreach (var t in PieceCatalog.AllTypes.Where(t => t != PieceType.Flag))
                if (tb.Allowed[(int)t]) Check(CombatTable.Resolve(PieceType.Airplane, t) == outcome, $"{t} consistent with outcome {outcome}");
            Check(tb.Notes.Any(n => n.Kind == NoteKind.Combat), "combat note recorded");
            CheckCounts(k);

            // --- ply 3: a one-step south move excludes mine and flag for that piece ---
            var sv = m.GetView(Side.South);
            var step = m.LegalMoves(Side.South).First(c => sv.OwnById(c.PieceId).Type != PieceType.Airplane);
            m.Apply(Side.South, step);
            cpu.Observe(m.GetView(Side.North));
            var cap = k.Belief(step.PieceId);
            Check(!cap.Allowed[(int)PieceType.Mine] && !cap.Allowed[(int)PieceType.Flag], "moved piece is not mine/flag");

            // --- enemy attack against a known CPU piece: south piece attacking north 少将 at (6,4) ---
            var m2 = new Match(Scenario.Build(Side.South, new Dictionary<(int, int), PieceType> { [(6, 3)] = PieceType.Airplane }, 0, 0),
                               Scenario.Build(Side.North, new Dictionary<(int, int), PieceType> { [(6, 4)] = PieceType.MajorGeneral }, 0, 7));
            var cpu2 = new CpuPlayer(Side.North, 3, 3);
            cpu2.Observe(m2.GetView(Side.North));
            int attacker = Scenario.IdAt(m2.GetView(Side.South), C(6, 3));
            var r2 = m2.Apply(Side.South, new MoveCommand(attacker, C(6, 4)));
            Check(r2.Combat.Outcome == CombatOutcome.DefenderWins, "airplane loses to 少将");
            cpu2.Observe(m2.GetView(Side.North));
            var ab = cpu2.Knowledge.Belief(attacker);
            Check(ab.Candidates().SequenceEqual(new[] { PieceType.Airplane }), "crossed the band without a gateway -> airplane");
            Check(!ab.Alive, "attacker removed");
            Check(ab.Notes.Any(n => n.Text.Contains("突入口を通らず")), "reason: band crossing");
        }

        public static void FormationStyles()
        {
            var counts = new int[5];
            for (int seed = 1; seed <= 1000; seed++)
            {
                var s = MilitaryShogi.Rules.FormationStyles.FromSeed(seed);
                Check(s == MilitaryShogi.Rules.FormationStyles.FromSeed(seed), "style from seed deterministic");
                counts[(int)s]++;
            }
            Check(counts.All(c => c > 140), "all five styles occur: " + string.Join(",", counts));
            var auto = new CpuPlayer(Side.North, 42, 1);
            Check(auto.Style == MilitaryShogi.Rules.FormationStyles.FromSeed(42) && !auto.StyleForced, "auto style");
            var forced = new CpuPlayer(Side.North, 42, 1, FormationStyle.Trap);
            Check(forced.Style == FormationStyle.Trap && forced.StyleForced, "forced style");
            Check(new CpuPlayer(Side.North, 42, 999, FormationStyle.Trap).CreateFormation().Signature() == forced.CreateFormation().Signature(),
                "formation independent of decision seed");
            Check(new CpuPlayer(Side.North, 43, 1, FormationStyle.Trap).CreateFormation().Signature() != forced.CreateFormation().Signature(),
                "formation depends on formation seed");
        }

        /// <summary>Plays CPU vs CPU and returns the move list.</summary>
        public static List<string> PlayCpuGame(int southFormation, int southDecision, int northFormation, int northDecision, int maxPlies, out Match match,
            out CpuPlayer southCpu, out CpuPlayer northCpu)
        {
            southCpu = new CpuPlayer(Side.South, southFormation, southDecision);
            northCpu = new CpuPlayer(Side.North, northFormation, northDecision);
            match = new Match(southCpu.CreateFormation(), northCpu.CreateFormation());
            var moves = new List<string>();
            while (match.Status == GameStatus.Playing && match.Ply < maxPlies)
            {
                var cpu = match.ToMove == Side.South ? southCpu : northCpu;
                var report = cpu.Decide(match.GetView(match.ToMove));
                var rec = match.Apply(match.ToMove, report.Chosen.Command);
                moves.Add(report.Chosen.Command.ToString() + (rec.Combat != null ? ":" + rec.Combat.Outcome : ""));
            }
            return moves;
        }

        public static void DecisionSeedReproducibility()
        {
            var a = PlayCpuGame(11, 21, 31, 41, 80, out _, out _, out _);
            var b = PlayCpuGame(11, 21, 31, 41, 80, out _, out _, out _);
            Check(a.SequenceEqual(b), "same seeds -> identical game");
            int differing = 0;
            for (int d = 1; d <= 5; d++)
            {
                var c = PlayCpuGame(11, 21, 31, 41 + d, 80, out var mc, out _, out var nc);
                if (!a.SequenceEqual(c)) differing++;
                Check(nc.CreateFormation().Signature() == new CpuPlayer(Side.North, 31, 41).CreateFormation().Signature(), "formation fixed while decision seed changes");
            }
            Check(differing >= 4, $"changing only the decision seed changes play ({differing}/5)");
            Metrics.Add($"decision seed variation: {differing}/5 alternative decision seeds produced a different game from the same formations");
        }
    }
}
