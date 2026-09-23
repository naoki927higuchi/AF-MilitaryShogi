using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MilitaryShogi.Engine;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;
using static MilitaryShogi.Tests.Program;

namespace MilitaryShogi.Tests
{
    internal static class PlayerFactsTests
    {
        private static int C(int x, int y) => BoardGraph.Cell(x, y);
        private sealed class PublicFixture
        {
            public List<OwnPieceView> Own = new List<OwnPieceView>();
            public List<EnemyPieceView> Enemy = new List<EnemyPieceView>();
            public List<ObservedMove> History = new List<ObservedMove>();
            public sbyte[] Owners = Enumerable.Repeat(MoveRules.Empty, BoardGraph.NodeCount).ToArray();
            public void AddOwn(int id, PieceType type, int node) { Own.Add(new OwnPieceView(id, id, type, node, node)); Owners[node] = (sbyte)Side.South; }
            public void AddEnemy(int id, int node) { Enemy.Add(new EnemyPieceView(id, id, node, node)); Owners[node] = (sbyte)Side.North; }
            public PlayerView View() => new PlayerView(Side.South, History.Count, Side.South, GameStatus.Playing, null, EndReason.None, Own.ToArray(), Enemy.ToArray(), History.ToArray(), Owners);
            public void Move(Side side, int id, PieceType movement, int from, int to, ObservedCombat combat = null)
            {
                var move = MoveRules.Generate(movement, side, from, Owners).Single(m => m.To == to);
                History.Add(new ObservedMove(History.Count + 1, side, id, from, to, move.Path, move.Jumped, Owners, combat));
                Owners[from] = MoveRules.Empty;
                if (combat == null || combat.Outcome == CombatOutcome.AttackerWins) Owners[to] = (sbyte)side;
                else if (combat.Outcome == CombatOutcome.Tie) Owners[to] = MoveRules.Empty;
            }
        }

        public static void MovementAndRemoval()
        {
            var f = new PublicFixture();
            f.AddOwn(1, PieceType.General, C(3, 0));
            f.AddEnemy(32, C(4, 7));
            f.AddEnemy(33, C(4, 6)); f.AddEnemy(34, C(4, 5)); f.AddEnemy(35, C(4, 4));
            var facts = new PlayerKnownFacts(f.View());
            Check(facts.KnownType(32) == null && facts.Identity(32) == "正体不明", "unobserved piece stays unknown");
            f.Move(Side.North, 32, PieceType.Airplane, C(4, 7), C(4, 0));
            Check(f.History[0].Jumped == 3, "e8 -> e1 over three pieces fixture");
            facts.Update(f.View());
            Check(facts.KnownType(32) == PieceType.Airplane && facts.Identity(32) == "判明：飛行機", "public jump identifies airplane");
            f.Move(Side.South, 1, PieceType.General, C(3, 0), C(4, 0), new ObservedCombat(1, 32, CombatOutcome.AttackerWins));
            facts.Update(f.View());
            Check(facts.KnownType(32) == PieceType.Airplane, "identification survives destruction");
            Check(new PlayerKnownFacts(f.View()).KnownType(32) == PieceType.Airplane, "history replay retains dead piece fact");
            facts.Update(f.View());
            Check(facts.KnownType(32) == PieceType.Airplane, "repeat observation is idempotent");

            f = new PublicFixture(); f.AddEnemy(32, C(1, 5));
            f.Move(Side.North, 32, PieceType.Engineer, C(1, 5), C(5, 5));
            Check(new PlayerKnownFacts(f.View()).KnownType(32) == PieceType.Engineer, "horizontal long move identifies engineer");
        }

        public static void CombatAndAmbiguity()
        {
            var f = new PublicFixture();
            f.AddOwn(1, PieceType.Colonel, C(0, 3));
            f.AddOwn(2, PieceType.LieutenantGeneral, C(2, 3));
            f.AddEnemy(32, C(0, 4));
            var facts = new PlayerKnownFacts(f.View());
            f.Move(Side.North, 32, PieceType.LieutenantGeneral, C(0, 4), BoardGraph.LeftCrossing);
            facts.Update(f.View());
            Check(facts.KnownType(32) == null && !facts.Candidates(32).Contains(PieceType.Airplane), "gateway excludes flyer but several candidates remain");
            f.Move(Side.South, 1, PieceType.Colonel, C(0, 3), BoardGraph.LeftCrossing, new ObservedCombat(1, 32, CombatOutcome.DefenderWins));
            facts.Update(f.View());
            Check(facts.Candidates(32).SequenceEqual(new[] { PieceType.General, PieceType.LieutenantGeneral, PieceType.MajorGeneral, PieceType.Tank }), "beating colonel leaves generals and tank");
            Check(facts.Identity(32) == "正体不明", "multiple possibilities never presented as certain");
            f.Move(Side.South, 2, PieceType.LieutenantGeneral, C(2, 3), BoardGraph.LeftCrossing, new ObservedCombat(2, 32, CombatOutcome.Tie));
            facts.Update(f.View());
            Check(facts.KnownType(32) == PieceType.LieutenantGeneral && facts.Identity(32) == "判明：中将", "combat plus movement proves lieutenant general even in a tie/removal");

            // Flag can imitate a backing piece: a combat alone must not falsely identify it.
            f = new PublicFixture(); f.AddOwn(1, PieceType.Airplane, C(3, 3)); f.AddEnemy(32, C(3, 5)); f.AddEnemy(33, C(3, 6));
            f.Move(Side.South, 1, PieceType.Airplane, C(3, 3), C(3, 5), new ObservedCombat(1, 32, CombatOutcome.Tie));
            facts = new PlayerKnownFacts(f.View());
            Check(facts.Candidates(32).Contains(PieceType.Flag), "flag with compatible unknown backer remains possible");
            Check(facts.KnownType(32) == null, "ambiguous flag strength cannot identify a kind");
        }

        public static void BoundaryAndSoundness()
        {
            var refs = typeof(PlayerKnownFacts).Assembly.GetReferencedAssemblies().Select(a => a.Name);
            Check(!refs.Contains("MilitaryShogi.Engine") && !refs.Contains("MilitaryShogi.Cpu"), "human facts cannot reference engine or CPU");
            var south = FormationGenerator.Generate(Side.South, FormationStyle.Balanced, 99);
            var north = FormationGenerator.Generate(Side.North, FormationStyle.Balanced, 88);
            var cells = north.Pieces.Where(p => p.Value == PieceType.General || p.Value == PieceType.LieutenantGeneral).Select(p => p.Key).ToArray();
            var swapped = north.Clone();
            Check(swapped.TryMoveOrSwap(cells[0], cells[1]), "swap true kinds preserving occupancy");
            var a = new Match(south, north); var b = new Match(south, swapped);
            var fa = new PlayerKnownFacts(a.GetView(Side.South)); var fb = new PlayerKnownFacts(b.GetView(Side.South));
            var quiet = a.LegalMoves(Side.South).First(m => a.GetView(Side.South).Owners[m.To] == MoveRules.Empty);
            a.Apply(Side.South, quiet); b.Apply(Side.South, quiet);
            fa.Update(a.GetView(Side.South)); fb.Update(b.GetView(Side.South));
            foreach (var e in a.GetView(Side.South).Enemy)
                Check(fa.Candidates(e.Id).SequenceEqual(fb.Candidates(e.Id)) && fa.Identity(e.Id) == fb.Identity(e.Id), "hidden kind swap cannot alter public facts");

            // Audit every historical prefix against the post-game test-only truth, on real legal games.
            for (int seed = 0; seed < 8; seed++)
            {
                var match = new Match(FormationGenerator.Generate(Side.South, FormationStyle.Balanced, 410 + seed), FormationGenerator.Generate(Side.North, FormationStyle.Trap, 510 + seed));
                var rng = new DeterministicRandom((ulong)(610 + seed));
                var snapshots = new List<PlayerView>();
                while (match.Status != GameStatus.Finished)
                {
                    var moves = match.LegalMoves(match.ToMove);
                    match.Apply(match.ToMove, moves[rng.Next(moves.Count)]);
                    snapshots.Add(match.GetView(Side.South));
                }
                var human = new PlayerKnownFacts(snapshots[0]);
                foreach (var view in snapshots)
                {
                    human.Update(view);
                    foreach (var e in view.Enemy)
                        Check(human.Candidates(e.Id).Contains(match.RevealAfterGameEnd(e.Id)), "public inference must retain true possibility, including flags and dead pieces");
                }
            }
        }
    }
}
