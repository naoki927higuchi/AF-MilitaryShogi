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
    internal static class GameTests
    {
        /// <summary>Baseline opponent: captures HQ if possible, attacks with strong pieces, else advances.</summary>
        public static MoveCommand GreedyMove(Match match, DeterministicRandom rng)
        {
            var side = match.ToMove;
            var view = match.GetView(side);
            var moves = match.LegalMoves(side);
            var enemyHq = BoardGraph.Headquarters(side.Opponent());
            MoveCommand best = moves[0];
            double bestScore = double.NegativeInfinity;
            foreach (var m in moves)
            {
                var type = view.OwnById(m.PieceId).Type;
                int from = view.OwnById(m.PieceId).Node;
                double score = rng.NextDouble() * 0.5;
                bool attack = view.Owners[m.To] != MoveRules.Empty;
                if (PieceCatalog.CanCaptureHeadquarters(type) && enemyHq.Contains(m.To)) score += 100;
                if (attack) score += type <= PieceType.Colonel || type == PieceType.Airplane ? 3 : type == PieceType.Tank ? 1.5 : -1;
                score += 0.3 * (BoardGraph.DistanceToHeadquarters(from, side.Opponent()) - BoardGraph.DistanceToHeadquarters(m.To, side.Opponent()));
                if (score > bestScore) { bestScore = score; best = m; }
            }
            return best;
        }

        /// <summary>
        /// After the game (when kinds may be revealed to the test), verify every belief the CPU
        /// held at every decision: the true kind was never excluded. Also measure how much
        /// probability the CPU put on the truth compared with the uninformed prior.
        /// </summary>
        private static void AuditBeliefs(Match match, CpuPlayer cpu, ref double sumTrue, ref double sumPrior, ref int samples)
        {
            foreach (var report in cpu.Reports)
                foreach (var b in report.Beliefs)
                {
                    var truth = match.RevealAfterGameEnd(b.Id);
                    Check(b.Allowed[(int)truth], $"ply {report.Ply}: {cpu.Side} excluded the true kind {truth} of enemy #{b.Number}");
                }
            // Accuracy at the end of the game, over pieces that interacted (moved or fought).
            foreach (var b in cpu.Knowledge.Enemies)
            {
                if (b.MoveCount == 0 && b.CombatCount == 0) continue;
                var truth = match.RevealAfterGameEnd(b.Id);
                sumTrue += b.P(truth);
                sumPrior += PieceCatalog.Count(truth) / 31.0;
                samples++;
            }
        }

        public static void FullGames(bool quick)
        {
            int games = quick ? 6 : 24;
            var results = new Dictionary<string, int>();
            int southWins = 0, northWins = 0, draws = 0, totalPlies = 0;
            double maxThink = 0, sumThink = 0; int decisions = 0;
            double sumTrue = 0, sumPrior = 0; int samples = 0;
            for (int g = 0; g < games; g++)
            {
                CpuTests.PlayCpuGame(100 + g, 200 + g, 300 + g, 400 + g, 10000, out var match, out var s, out var n);
                Check(match.Status == GameStatus.Finished, "CPU vs CPU game finished");
                string key = match.EndReason.ToString();
                results[key] = results.TryGetValue(key, out var c) ? c + 1 : 1;
                if (match.Winner == Side.South) southWins++; else if (match.Winner == Side.North) northWins++; else draws++;
                totalPlies += match.Ply;
                foreach (var r in s.Reports.Concat(n.Reports)) { maxThink = Math.Max(maxThink, r.ThinkMilliseconds); sumThink += r.ThinkMilliseconds; decisions++; }
                AuditBeliefs(match, s, ref sumTrue, ref sumPrior, ref samples);
                AuditBeliefs(match, n, ref sumTrue, ref sumPrior, ref samples);
            }
            Metrics.Add($"CPU vs CPU {games} games: South {southWins} / North {northWins} / draw {draws}; endings {string.Join(", ", results.Select(kv => kv.Key + "=" + kv.Value))}; avg {totalPlies / (double)games:0} plies");
            Metrics.Add($"think time avg {sumThink / Math.Max(1, decisions):0.0} ms, max {maxThink:0.0} ms over {decisions} decisions");
            Metrics.Add($"final belief on the true kind (pieces that moved/fought): {sumTrue / samples:P1} vs uninformed prior {sumPrior / samples:P1} (n={samples})");
            Check(sumTrue / samples > 2 * sumPrior / samples, "beliefs are informative");

            // CPU vs a random mover: the CPU should win almost always.
            int cpuWins = 0, randomGames = quick ? 6 : 20;
            for (int g = 0; g < randomGames; g++)
            {
                var cpuSide = g % 2 == 0 ? Side.North : Side.South;
                var cpu = new CpuPlayer(cpuSide, 500 + g, 600 + g);
                var other = FormationGenerator.Generate(cpuSide.Opponent(), (FormationStyle)(g % 5), 700 + g);
                var match = cpuSide == Side.North ? new Match(other, cpu.CreateFormation()) : new Match(cpu.CreateFormation(), other);
                var rng = new DeterministicRandom(800 + g);
                while (match.Status == GameStatus.Playing)
                {
                    if (match.ToMove == cpuSide) match.Apply(cpuSide, cpu.Decide(match.GetView(cpuSide)).Chosen.Command);
                    else
                    {
                        var moves = match.LegalMoves(match.ToMove);
                        match.Apply(match.ToMove, moves[rng.Next(moves.Count)]);
                    }
                }
                if (match.Winner == cpuSide) cpuWins++;
                double t = 0, p = 0; int n = 0;
                AuditBeliefs(match, cpu, ref t, ref p, ref n);
            }
            Metrics.Add($"CPU vs random mover: CPU won {cpuWins}/{randomGames}");
            Check(cpuWins >= randomGames * 0.8, $"CPU beats a random mover ({cpuWins}/{randomGames})");

            // CPU vs an aggressive baseline that sees its own kinds: attacks whenever its piece is
            // guaranteed not to lose to anything it could meet... it cannot know that, so it attacks
            // with officers of rank 大佐 or better and otherwise advances toward the enemy HQ.
            int vsGreedy = 0, greedyGames = quick ? 6 : 20, greedyPlies = 0;
            for (int g = 0; g < greedyGames; g++)
            {
                var cpuSide = g % 2 == 0 ? Side.North : Side.South;
                var cpu = new CpuPlayer(cpuSide, 900 + g, 950 + g);
                var other = FormationGenerator.Generate(cpuSide.Opponent(), (FormationStyle)(g % 5), 990 + g);
                var match = cpuSide == Side.North ? new Match(other, cpu.CreateFormation()) : new Match(cpu.CreateFormation(), other);
                var rng = new DeterministicRandom(1300 + g);
                while (match.Status == GameStatus.Playing)
                {
                    if (match.ToMove == cpuSide) match.Apply(cpuSide, cpu.Decide(match.GetView(cpuSide)).Chosen.Command);
                    else match.Apply(match.ToMove, GreedyMove(match, rng));
                }
                greedyPlies += match.Ply;
                if (match.Winner == cpuSide) vsGreedy++;
            }
            Metrics.Add($"CPU vs greedy baseline: CPU won {vsGreedy}/{greedyGames}, avg {greedyPlies / (double)greedyGames:0} plies");
            Check(vsGreedy >= greedyGames * 0.6, $"CPU beats the greedy baseline ({vsGreedy}/{greedyGames})");
        }
    }
}
