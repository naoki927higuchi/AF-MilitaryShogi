using System;
using System.Collections.Generic;
using System.Linq;
using MilitaryShogi.Engine;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;
using static MilitaryShogi.Tests.Program;

namespace MilitaryShogi.Tests
{
    internal static class Scenario
    {
        /// <summary>A valid formation with some pieces pinned to (x, y) cells; the rest fill the camp deterministically.</summary>
        public static Formation Build(Side side, Dictionary<(int x, int y), PieceType> pinned, int emptyX, int emptyY)
        {
            var pieces = new Dictionary<int, PieceType>();
            var army = PieceCatalog.StandardArmy();
            foreach (var p in pinned)
            {
                pieces[BoardGraph.Cell(p.Key.x, p.Key.y)] = p.Value;
                army.Remove(p.Value);
            }
            int empty = BoardGraph.Cell(emptyX, emptyY);
            // Mines and flag first so they get legal cells.
            army = army.OrderBy(t => t == PieceType.Mine || t == PieceType.Flag ? 0 : 1).ToList();
            foreach (var t in army)
            {
                int cell = BoardGraph.CampCells(side).First(n => n != empty && !pieces.ContainsKey(n) && PlacementRules.IsAllowed(t, n, side));
                pieces[cell] = t;
            }
            var f = new Formation(side, pieces);
            PlacementRules.Validate(f);
            return f;
        }

        public static int IdAt(PlayerView v, int node)
        {
            var own = v.Own.FirstOrDefault(p => p.Node == node);
            if (own != null) return own.Id;
            var e = v.Enemy.FirstOrDefault(p => p.Node == node);
            return e != null ? e.Id : -1;
        }

        /// <summary>Make any quiet south move that does not touch the given columns.</summary>
        public static void QuietMove(Match m, Side side, params int[] avoidColumns)
        {
            var v = m.GetView(side);
            foreach (var cmd in m.LegalMoves(side))
            {
                var p = v.OwnById(cmd.PieceId);
                if (avoidColumns.Contains(BoardGraph.X(p.Node)) || avoidColumns.Contains(BoardGraph.X(cmd.To))) continue;
                if (v.Owners[cmd.To] != MoveRules.Empty) continue;
                m.Apply(side, cmd);
                return;
            }
            throw new Exception("no quiet move");
        }
    }

    internal static class EngineTests
    {
        private static int C(int x, int y) => BoardGraph.Cell(x, y);

        private static ObservedMove AirStrike(PieceType flagBacker, PieceType target, bool targetIsFlag, out Match match)
        {
            // South: target at (3,1); if flag, backer at (3,0). North airplane at (3,4) flies down column 3.
            var south = new Dictionary<(int, int), PieceType> { [(3, 1)] = targetIsFlag ? PieceType.Flag : target };
            if (targetIsFlag) south[(3, 0)] = flagBacker;
            var north = new Dictionary<(int, int), PieceType> { [(3, 4)] = PieceType.Airplane };
            match = new Match(Scenario.Build(Side.South, south, 6, 2), Scenario.Build(Side.North, north, 6, 5));
            Scenario.QuietMove(match, Side.South, 3);
            var nv = match.GetView(Side.North);
            int airplane = Scenario.IdAt(nv, C(3, 4));
            return match.Apply(Side.North, new MoveCommand(airplane, C(3, 1)));
        }

        public static void JudgeScenarios()
        {
            Match m;
            var r = AirStrike(PieceType.General, PieceType.Flag, true, out m);
            Check(r.Combat != null && r.Combat.Outcome == CombatOutcome.DefenderWins, "flag backed by 大将 beats airplane");
            Check(r.Jumped == 2, "airplane jumped two pieces");
            r = AirStrike(PieceType.SecondLieutenant, PieceType.Flag, true, out m);
            Check(r.Combat.Outcome == CombatOutcome.AttackerWins, "flag backed by 少尉 loses to airplane");
            Check(m.GetView(Side.South).Owners[C(3, 0)] == (sbyte)Side.South, "backer unaffected");
            r = AirStrike(PieceType.Mine, PieceType.Flag, true, out m);
            Check(r.Combat.Outcome == CombatOutcome.AttackerWins, "flag backed by mine loses to airplane (as mine)");
            r = AirStrike(PieceType.Captain, PieceType.Mine, false, out m);
            Check(r.Combat.Outcome == CombatOutcome.AttackerWins, "airplane beats mine");
            r = AirStrike(PieceType.Captain, PieceType.MajorGeneral, false, out m);
            Check(r.Combat.Outcome == CombatOutcome.DefenderWins, "少将 beats airplane");
            Check(m.GetView(Side.North).Own.First(p => p.Type == PieceType.Airplane && p.InitialNode == C(3, 4)).Node == -1, "loser removed");
            r = AirStrike(PieceType.Captain, PieceType.Airplane, false, out m);
            Check(r.Combat.Outcome == CombatOutcome.Tie, "airplane vs airplane tie");
            var sv = m.GetView(Side.South);
            Check(sv.Owners[C(3, 1)] == MoveRules.Empty, "tie empties the square");

            // Views never expose enemy kinds; both sides see the same public record.
            Check(m.GetView(Side.North).History.Last().Combat.Outcome == sv.History.Last().Combat.Outcome, "same public record");

            // Flag strength is evaluated at combat time: the 大将 behind it steps aside, the flag is now alone.
            var south = new Dictionary<(int, int), PieceType> { [(3, 1)] = PieceType.Flag, [(3, 0)] = PieceType.General };
            var north = new Dictionary<(int, int), PieceType> { [(3, 4)] = PieceType.Airplane };
            m = new Match(Scenario.Build(Side.South, south, 2, 0), Scenario.Build(Side.North, north, 6, 5));
            m.Apply(Side.South, new MoveCommand(Scenario.IdAt(m.GetView(Side.South), C(3, 0)), C(2, 0)));
            r = m.Apply(Side.North, new MoveCommand(Scenario.IdAt(m.GetView(Side.North), C(3, 4)), C(3, 1)));
            Check(r.Combat.Outcome == CombatOutcome.AttackerWins, "unbacked flag loses");

            // Illegal moves are rejected.
            bool threw = false;
            try { m.Apply(Side.North, new MoveCommand(0, C(0, 0))); } catch (Exception) { threw = true; }
            Check(threw, "wrong side / illegal move rejected");
            threw = false;
            try { m.RevealAfterGameEnd(0); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "kinds stay hidden during the game");
        }

        public static void HeadquartersRule()
        {
            // North airplane flies into an empty south HQ cell: not a capture.
            var south = new Dictionary<(int, int), PieceType> { [(3, 0)] = PieceType.Captain };
            var north = new Dictionary<(int, int), PieceType> { [(3, 4)] = PieceType.Airplane };
            var m = new Match(Scenario.Build(Side.South, south, 2, 0), Scenario.Build(Side.North, north, 6, 5));
            m.Apply(Side.South, new MoveCommand(Scenario.IdAt(m.GetView(Side.South), C(3, 0)), C(2, 0)));   // vacate the HQ cell
            var r = m.Apply(Side.North, new MoveCommand(Scenario.IdAt(m.GetView(Side.North), C(3, 4)), C(3, 0)));
            Check(r.Combat == null && m.Status == GameStatus.Playing, "airplane in HQ does not win");

            // Random games: every HQ-capture win is by 大将〜少佐 standing in the enemy HQ.
            int captures = 0;
            for (int seed = 1; seed <= 400 && captures < 25; seed++)
            {
                var game = RandomGame(seed, out var lastMover);
                if (game.EndReason != EndReason.HeadquartersCaptured) continue;
                captures++;
                var last = game.History.Last();
                Check(game.Winner == last.Mover, "capturer wins");
                Check(BoardGraph.IsHeadquarters(last.To, last.Mover.Opponent()), "ended on enemy HQ");
                Check(PieceCatalog.CanCaptureHeadquarters(game.RevealAfterGameEnd(last.PieceId)), "capturer is an officer 大将〜少佐");
            }
            Check(captures > 0, "some random game ended by HQ capture");
        }

        public static Match RandomGame(int seed, out Side lastMover, int maxPlies = 2000)
        {
            var rng = new DeterministicRandom(seed);
            var m = new Match(FormationGenerator.Generate(Side.South, (FormationStyle)(seed % 5), seed),
                              FormationGenerator.Generate(Side.North, (FormationStyle)((seed / 5) % 5), seed + 1000),
                              new MatchConfig { MaxPlies = maxPlies, MaxPliesWithoutCombat = maxPlies });
            lastMover = Side.South;
            while (m.Status == GameStatus.Playing)
            {
                var moves = m.LegalMoves(m.ToMove);
                // Bias towards attacks and forward moves so games end.
                var view = m.GetView(m.ToMove);
                var attacks = moves.Where(c => view.Owners[c.To] != MoveRules.Empty).ToList();
                var pick = attacks.Count > 0 && rng.NextDouble() < 0.5 ? attacks[rng.Next(attacks.Count)] : moves[rng.Next(moves.Count)];
                lastMover = m.ToMove;
                m.Apply(m.ToMove, pick);
            }
            return m;
        }

        private static int CapturersAlive(Match m, Side side)
        {
            return m.GetView(side).Own.Count(p => p.Alive && PieceCatalog.CanCaptureHeadquarters(p.Type));
        }

        /// <summary>
        /// 1.3.0: as soon as neither side has 大将〜少佐 left the game is a draw (NoCapturers); never
        /// earlier. The same games under the old rule continue past that ply.
        /// </summary>
        public static void NoCapturersDraw()
        {
            int found = 0;
            for (int seed = 1; seed <= 400 && found < 12; seed++)
            {
                var moves = new List<MoveCommand>();
                var rng = new DeterministicRandom(seed * 131);
                var m = new Match(FormationGenerator.Generate(Side.South, FormationStyle.Balanced, seed), FormationGenerator.Generate(Side.North, FormationStyle.Aggressive, seed),
                    new MatchConfig { DrawWhenNoCapturers = true });   // explicit, so the suite also holds under --legacy-draw
                while (m.Status == GameStatus.Playing)
                {
                    Check(CapturersAlive(m, Side.South) > 0 || CapturersAlive(m, Side.North) > 0, "never playing on with no capturer on either side");
                    var legal = m.LegalMoves(m.ToMove);
                    var cmd = legal[rng.Next(legal.Count)];
                    moves.Add(cmd);
                    m.Apply(m.ToMove, cmd);
                }
                if (m.EndReason != EndReason.NoCapturers) continue;
                found++;
                Check(m.Winner == null, "NoCapturers is a draw");
                Check(CapturersAlive(m, Side.South) == 0 && CapturersAlive(m, Side.North) == 0, "both sides really have no 大将〜少佐");
                var last = m.History[m.History.Count - 1];
                Check(last.Combat != null, "the draw is triggered by the combat that removed the last capturer");

                // Old rule: the same moves leave the game in progress at that ply.
                var legacy = new Match(FormationGenerator.Generate(Side.South, FormationStyle.Balanced, seed), FormationGenerator.Generate(Side.North, FormationStyle.Aggressive, seed),
                    new MatchConfig { DrawWhenNoCapturers = false });
                foreach (var c in moves) legacy.Apply(legacy.ToMove, c);
                Check(legacy.Status == GameStatus.Playing || legacy.EndReason != EndReason.NoCapturers, "legacy rule does not end by NoCapturers");
            }
            Check(found > 0, "random games reached a no-capturer position");
            Metrics.Add("1.3.0 no-capturer draws found in random games: " + found);
        }

        /// <summary>1.3.0: resignation ends the game immediately; the opponent wins with EndReason.Resigned.</summary>
        public static void Resignation()
        {
            var m = new Match(FormationGenerator.Generate(Side.South, FormationStyle.Balanced, 5), FormationGenerator.Generate(Side.North, FormationStyle.Balanced, 6));
            m.Apply(Side.South, m.LegalMoves(Side.South)[0]);
            m.Resign(Side.South);
            Check(m.Status == GameStatus.Finished && m.Winner == Side.North && m.EndReason == EndReason.Resigned, "south resigns → north wins by resignation");
            Check(m.GetView(Side.South).EndReason == EndReason.Resigned && m.GetView(Side.South).Winner == Side.North, "players see the reason");
            Check(m.LegalMoves(Side.North).Count == 0, "no moves after resignation");
            bool threw = false;
            try { m.Resign(Side.North); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "cannot resign a finished game");
        }

        public static void RandomGameInvariants()
        {
            for (int seed = 1; seed <= 60; seed++)
            {
                var rng = new DeterministicRandom(seed * 7);
                var m = new Match(FormationGenerator.Generate(Side.South, FormationStyle.Balanced, seed), FormationGenerator.Generate(Side.North, FormationStyle.Mobile, seed));
                while (m.Status == GameStatus.Playing)
                {
                    var side = m.ToMove;
                    var view = m.GetView(side);
                    var moves = m.LegalMoves(side);
                    Check(moves.Count > 0, "side to move has a move while playing");
                    foreach (var c in moves)
                    {
                        Check(view.Owners[c.To] != (sbyte)side, "never onto own piece");
                        var p = view.OwnById(c.PieceId);
                        Check(PieceCatalog.IsMobile(p.Type), "immobile pieces never move");
                    }
                    int aliveBefore = view.Own.Count(p => p.Alive) + view.Enemy.Count(p => p.Alive);
                    var r = m.Apply(side, moves[rng.Next(moves.Count)]);
                    var after = m.GetView(side);
                    int aliveAfter = after.Own.Count(p => p.Alive) + after.Enemy.Count(p => p.Alive);
                    int expectedLoss = r.Combat == null ? 0 : r.Combat.Outcome == CombatOutcome.Tie ? 2 : 1;
                    Check(aliveBefore - aliveAfter == expectedLoss, "removal count matches outcome");
                    Check(after.Owners.Count(o => o != MoveRules.Empty) == aliveAfter, "occupancy consistent");
                }
                Check(m.Status == GameStatus.Finished, "finished");
            }
        }
    }
}
