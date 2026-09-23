using System;
using System.Linq;
using MilitaryShogi.Cpu;
using MilitaryShogi.Engine;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;

namespace MilitaryShogi.Tests
{
    /// <summary>Ad-hoc analysis (--diag): how CPU games against the greedy baseline end.</summary>
    internal static class Diagnostics
    {
        public static void Trace(int g)
        {
            var cpuSide = g % 2 == 0 ? Side.North : Side.South;
            var cpu = new CpuPlayer(cpuSide, 900 + g, 950 + g);
            var other = FormationGenerator.Generate(cpuSide.Opponent(), (FormationStyle)(g % 5), 990 + g);
            var match = cpuSide == Side.North ? new Match(other, cpu.CreateFormation()) : new Match(cpu.CreateFormation(), other);
            var rng = new DeterministicRandom(1300 + g);
            var cf = cpu.CreateFormation();
            Console.WriteLine("CPU HQ: " + string.Join(",", BoardGraph.Headquarters(cpuSide).Select(h => cf.Pieces.TryGetValue(h, out var t) ? t.ToString() : "empty")));
            while (match.Status == GameStatus.Playing)
            {
                if (match.ToMove == cpuSide)
                {
                    var r = cpu.Decide(match.GetView(cpuSide));
                    var c = r.Chosen;
                    Console.WriteLine($"ply {match.Ply + 1} CPU {c.PieceType} {BoardGraph.Describe(c.From)}->{BoardGraph.Describe(c.Command.To)} score {c.Score:0.00} def {c.Terms.Defense:0.00} | {string.Join(" ; ", r.Considerations)}");
                    foreach (var alt in r.Candidates.Take(4)) Console.WriteLine($"     alt {alt.PieceType} {BoardGraph.Describe(alt.From)}->{BoardGraph.Describe(alt.Command.To)} {alt.Score:0.00} def {alt.Terms.Defense:0.00} saf {alt.Terms.Safety:0.00} mat {alt.Terms.Material:0.00}");
                    match.Apply(cpuSide, c.Command);
                }
                else
                {
                    var m = GameTests.GreedyMove(match, rng);
                    var v = match.GetView(match.ToMove);
                    var rec = match.Apply(match.ToMove, m);
                    Console.WriteLine($"ply {rec.Ply} OPP {v.OwnById(m.PieceId).Type} {BoardGraph.Describe(rec.From)}->{BoardGraph.Describe(rec.To)} {(rec.Combat != null ? rec.Combat.Outcome.ToString() : "")}");
                }
            }
        }

        public static void Run()
        {
            int wins = 0, n = 40;
            for (int g = 0; g < n; g++)
            {
                var cpuSide = g % 2 == 0 ? Side.North : Side.South;
                var cpu = new CpuPlayer(cpuSide, 900 + g, 950 + g);
                var other = FormationGenerator.Generate(cpuSide.Opponent(), (FormationStyle)(g % 5), 990 + g);
                var match = cpuSide == Side.North ? new Match(other, cpu.CreateFormation()) : new Match(cpu.CreateFormation(), other);
                var rng = new DeterministicRandom(1300 + g);
                while (match.Status == GameStatus.Playing)
                {
                    if (match.ToMove == cpuSide) match.Apply(cpuSide, cpu.Decide(match.GetView(cpuSide)).Chosen.Command);
                    else match.Apply(match.ToMove, GameTests.GreedyMove(match, rng));
                }
                bool won = match.Winner == cpuSide;
                if (won) wins++;
                var v = match.GetView(cpuSide);
                var last = match.History.Last();
                Console.WriteLine($"game {g,2} cpu={cpuSide} style={FormationStyles.JapaneseName(cpu.Style)} {(won ? "WIN " : match.Winner == null ? "DRAW" : "LOSS")} {match.EndReason} plies={match.Ply} " +
                    $"cpuAlive={v.Own.Count(p => p.Alive)} enemyAlive={v.Enemy.Count(p => p.Alive)} lastMover={last.Mover} capturer={(match.EndReason == EndReason.HeadquartersCaptured ? match.RevealAfterGameEnd(last.PieceId).ToString() : "-")}");
            }
            Console.WriteLine($"CPU won {wins}/{n}");
        }
    }
}
