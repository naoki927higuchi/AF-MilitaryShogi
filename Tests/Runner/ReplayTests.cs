using System;
using System.Collections.Generic;
using System.Linq;
using MilitaryShogi.Engine;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;
using static MilitaryShogi.Tests.Program;

namespace MilitaryShogi.Tests
{
    /// <summary>1.5.0 棋譜再現: a TURN rebuilt from the final view equals the view the player had then.</summary>
    internal static class ReplayTests
    {
        private static string Facts(PlayerKnownFacts f, PlayerView v)
            => string.Join(";", v.Enemy.Select(e => e.Id + "=" + string.Join(",", f.Candidates(e.Id))));

        private static string Pieces(PlayerView v)
            => string.Join(";", v.Own.Select(p => p.Id + "@" + p.Node)) + "|" + string.Join(";", v.Enemy.Select(e => e.Id + "@" + e.Node));

        /// <summary>Plays random games, snapshots the South view and incremental knowledge every ply, then compares every TURN.</summary>
        public static void ReplayMatchesLiveViews()
        {
            for (int g = 0; g < 8; g++)
            {
                var south = FormationGenerator.Generate(Side.South, (FormationStyle)(g % 5), 11 + g);
                var north = FormationGenerator.Generate(Side.North, (FormationStyle)((g + 2) % 5), 31 + g);
                var match = new Match(south, north);
                var rng = new DeterministicRandom(51 + g);
                var views = new List<PlayerView> { match.GetView(Side.South) };
                var live = new PlayerKnownFacts(views[0]);
                var facts = new List<string> { Facts(live, views[0]) };
                bool resign = g == 7;
                while (match.Status == GameStatus.Playing)
                {
                    if (resign && match.Ply == 30) { match.Resign(Side.South); break; }
                    var moves = match.LegalMoves(match.ToMove);
                    match.Apply(match.ToMove, g % 2 == 0 ? moves[rng.Next(moves.Count)] : GameTests.GreedyMove(match, rng));
                    var v = match.GetView(Side.South);
                    views.Add(v); live.Update(v); facts.Add(Facts(live, v));
                }
                var final = match.GetView(Side.South);
                int n = GameReplay.Length(final);
                Check(n == final.History.Count && n == views.Count - 1, $"game {g}: last TURN equals the number of plies ({n})");
                Check(ReferenceEquals(GameReplay.ViewAt(final, n), final), $"game {g}: last TURN is the final view");
                for (int t = 0; t <= n; t++)
                {
                    var r = GameReplay.ViewAt(final, t);
                    var s = views[t];
                    Check(Pieces(r) == Pieces(s), $"game {g} TURN {t}: piece positions and losses equal the live view");
                    Check(r.Owners.SequenceEqual(s.Owners), $"game {g} TURN {t}: board owners equal the live view");
                    Check(r.History.Count == t && r.Ply == s.Ply, $"game {g} TURN {t}: history holds exactly {t} plies");
                    if (t < n) Check(r.ToMove == s.ToMove && r.Status == GameStatus.Playing, $"game {g} TURN {t}: side to move");
                    // Knowledge at TURN t = what the player knew then (no later observation leaks back).
                    Check(Facts(new PlayerKnownFacts(r), r) == facts[t], $"game {g} TURN {t}: knowledge equals the live knowledge");
                }
                var t0 = GameReplay.ViewAt(final, 0);
                Check(t0.Own.All(p => p.Node == p.InitialNode) && t0.Enemy.All(e => e.Node == e.InitialNode), $"game {g}: TURN 0 is the initial placement");
                Check(t0.Enemy.All(e => new PlayerKnownFacts(t0).KnownType(e.Id) == null), $"game {g}: nothing is known at TURN 0");
                // Losses appear in removal order: the order of the first TURN a piece is gone.
                var order = new List<int>();
                for (int t = 1; t <= n; t++)
                    foreach (var e in GameReplay.ViewAt(final, t).Enemy.Where(e => !e.Alive && !order.Contains(e.Id))) order.Add(e.Id);
                Check(order.Count == final.Enemy.Count(e => !e.Alive), $"game {g}: every enemy loss has a removal TURN");
            }
            bool threw = false;
            try { GameReplay.ViewAt(new Match(FormationGenerator.Generate(Side.South, FormationStyle.Defensive, 1), FormationGenerator.Generate(Side.North, FormationStyle.Defensive, 2)).GetView(Side.South), 1); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw, "a TURN beyond the game is rejected");
        }
    }
}
