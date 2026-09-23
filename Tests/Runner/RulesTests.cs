using System;
using System.Collections.Generic;
using System.Linq;
using MilitaryShogi.Rules;
using static MilitaryShogi.Tests.Program;

namespace MilitaryShogi.Tests
{
    internal static class RulesTests
    {
        public static void ArmyComposition()
        {
            var army = PieceCatalog.StandardArmy();
            Check(army.Count == 31, "31 pieces");
            var expected = new Dictionary<PieceType, int>
            {
                [PieceType.General] = 1, [PieceType.LieutenantGeneral] = 1, [PieceType.MajorGeneral] = 2,
                [PieceType.Colonel] = 2, [PieceType.LieutenantColonel] = 2, [PieceType.Major] = 2,
                [PieceType.Captain] = 2, [PieceType.FirstLieutenant] = 2, [PieceType.SecondLieutenant] = 2,
                [PieceType.Airplane] = 2, [PieceType.Tank] = 3, [PieceType.Cavalry] = 2, [PieceType.Engineer] = 3,
                [PieceType.Spy] = 1, [PieceType.Mine] = 3, [PieceType.Flag] = 1,
            };
            foreach (var kv in expected) Check(army.Count(t => t == kv.Key) == kv.Value, PieceCatalog.JapaneseName(kv.Key) + " count");
            Check(expected.Values.Sum() == 31, "spec sums to 31");
            Check(BoardGraph.CampCells(Side.South).Count() == 32 && BoardGraph.CampCells(Side.North).Count() == 32, "32 cells per camp");
            var capturers = PieceCatalog.AllTypes.Where(PieceCatalog.CanCaptureHeadquarters).ToList();
            Check(capturers.SequenceEqual(new[] { PieceType.General, PieceType.LieutenantGeneral, PieceType.MajorGeneral, PieceType.Colonel, PieceType.LieutenantColonel, PieceType.Major }), "HQ capturers");
        }

        // Expected result derived independently from the textual rules, to cross-check the transcribed table.
        private static int Expected(PieceType a, PieceType b)
        {
            if (a == b) return 0;
            bool IsOfficer(PieceType t) => t <= PieceType.SecondLieutenant;
            bool IsGeneralRank(PieceType t) => t <= PieceType.MajorGeneral;
            if (a == PieceType.Mine) return b == PieceType.Airplane || b == PieceType.Engineer ? -1 : 0;
            if (b == PieceType.Mine) return a == PieceType.Airplane || a == PieceType.Engineer ? 1 : 0;
            if (a == PieceType.Spy) return b == PieceType.General ? 1 : -1;
            if (b == PieceType.Spy) return a == PieceType.General ? -1 : 1;
            if (IsOfficer(a) && IsOfficer(b)) return a < b ? 1 : -1;
            if (IsOfficer(a)) return -Expected(b, a);
            // a is special (airplane/tank/cavalry/engineer), b is officer or special
            switch (a)
            {
                case PieceType.Airplane:
                    if (IsOfficer(b)) return IsGeneralRank(b) ? -1 : 1;
                    return 1; // tank, cavalry, engineer
                case PieceType.Tank:
                    if (IsOfficer(b)) return IsGeneralRank(b) ? -1 : 1;
                    if (b == PieceType.Airplane || b == PieceType.Engineer) return -1;
                    return 1; // cavalry
                case PieceType.Cavalry:
                    if (IsOfficer(b)) return -1;
                    return b == PieceType.Engineer ? 1 : -1;
                case PieceType.Engineer:
                    if (IsOfficer(b)) return -1;
                    return b == PieceType.Tank ? 1 : -1;
            }
            throw new InvalidOperationException();
        }

        public static void CombatTableMatchesRules()
        {
            var kinds = PieceCatalog.AllTypes.Where(t => t != PieceType.Flag).ToList();
            foreach (var a in kinds)
                foreach (var b in kinds)
                {
                    if (a == PieceType.Mine && b == PieceType.Mine) continue;
                    Check(CombatTable.Compare(a, b) == Expected(a, b), $"table {a} vs {b}: {CombatTable.Compare(a, b)} expected {Expected(a, b)}");
                    Check(CombatTable.Compare(a, b) == -CombatTable.Compare(b, a), $"antisymmetric {a} {b}");
                }
            // A few spot checks straight from the printed table.
            Check(CombatTable.Resolve(PieceType.Spy, PieceType.General) == CombatOutcome.AttackerWins, "spy attacks general");
            Check(CombatTable.Resolve(PieceType.General, PieceType.Spy) == CombatOutcome.DefenderWins, "general attacks spy");
            Check(CombatTable.Resolve(PieceType.General, PieceType.Mine) == CombatOutcome.Tie, "general vs mine tie");
            Check(CombatTable.Resolve(PieceType.Tank, PieceType.Colonel) == CombatOutcome.AttackerWins, "tank beats colonel");
            Check(CombatTable.Resolve(PieceType.Engineer, PieceType.Tank) == CombatOutcome.AttackerWins, "engineer beats tank");
            Check(CombatTable.Resolve(PieceType.Cavalry, PieceType.Engineer) == CombatOutcome.AttackerWins, "cavalry beats engineer");
            Check(CombatTable.Resolve(PieceType.Airplane, PieceType.Mine) == CombatOutcome.AttackerWins, "airplane beats mine");
            Check(CombatTable.Resolve(PieceType.Captain, null) == CombatOutcome.AttackerWins, "unbacked flag loses");
            bool threw = false;
            try { CombatTable.Compare(PieceType.Flag, PieceType.Captain); } catch (ArgumentException) { threw = true; }
            Check(threw, "flag must be resolved via FlagRule");
            // Flag backing cell: towards own back row.
            Check(FlagRule.BackingCell(BoardGraph.Cell(3, 1), Side.South) == BoardGraph.Cell(3, 0), "south flag backing");
            Check(FlagRule.BackingCell(BoardGraph.Cell(3, 6), Side.North) == BoardGraph.Cell(3, 7), "north flag backing");
            Check(FlagRule.BackingCell(BoardGraph.Cell(3, 0), Side.South) == -1, "back row has no backing");
        }

        public static void BoardGraphStructure()
        {
            for (int n = 0; n < BoardGraph.NodeCount; n++)
                foreach (int m in BoardGraph.Neighbors(n))
                    Check(BoardGraph.Neighbors(m).Contains(n), $"adjacency symmetric {n}-{m}");
            Check(BoardGraph.Neighbors(BoardGraph.LeftCrossing).OrderBy(x => x).SequenceEqual(new[] { BoardGraph.Cell(0, 3), BoardGraph.Cell(2, 3), BoardGraph.Cell(0, 4), BoardGraph.Cell(2, 4) }.OrderBy(x => x)), "left crossing arms");
            Check(BoardGraph.Neighbors(BoardGraph.RightCrossing).OrderBy(x => x).SequenceEqual(new[] { BoardGraph.Cell(5, 3), BoardGraph.Cell(7, 3), BoardGraph.Cell(5, 4), BoardGraph.Cell(7, 4) }.OrderBy(x => x)), "right crossing arms");
            // No direct link across the band.
            for (int x = 0; x < 8; x++) Check(!BoardGraph.Neighbors(BoardGraph.Cell(x, 3)).Contains(BoardGraph.Cell(x, 4)), "band blocks column " + x);
            Check(BoardGraph.Headquarters(Side.South).SequenceEqual(new[] { BoardGraph.Cell(3, 0), BoardGraph.Cell(4, 0) }), "south HQ");
            Check(BoardGraph.Headquarters(Side.North).SequenceEqual(new[] { BoardGraph.Cell(3, 7), BoardGraph.Cell(4, 7) }), "north HQ");
            Check(BoardGraph.CampCells(Side.South).Count(BoardGraph.IsArmEnd) == 4 && BoardGraph.CampCells(Side.North).Count(BoardGraph.IsArmEnd) == 4, "4 arm ends per camp");
            // Mirror symmetry of the board (x -> 7-x, y -> 7-y).
            for (int n = 0; n < BoardGraph.CellCount; n++)
            {
                int mirror = BoardGraph.Cell(7 - BoardGraph.X(n), 7 - BoardGraph.Y(n));
                Check(BoardGraph.IsArmEnd(n) == BoardGraph.IsArmEnd(mirror), "arm end symmetry");
                Check(BoardGraph.Neighbors(n).Count == BoardGraph.Neighbors(mirror).Count, "degree symmetry");
            }
            // Shortest path from a south arm end to the enemy front goes through a crossing: 2 steps.
            Check(BoardGraph.Distance(BoardGraph.Cell(0, 3), BoardGraph.Cell(2, 4)) == 2, "diagonal crossing distance");
            Check(BoardGraph.Distance(BoardGraph.Cell(3, 3), BoardGraph.Cell(3, 4)) == 4, "must detour to a gateway");
            Check(BoardGraph.DistanceToHeadquarters(BoardGraph.Cell(3, 0), Side.North) == 10, "HQ to HQ distance");
        }

        private static sbyte[] EmptyBoard()
        {
            var o = new sbyte[BoardGraph.NodeCount];
            for (int i = 0; i < o.Length; i++) o[i] = MoveRules.Empty;
            return o;
        }

        private static HashSet<int> Targets(PieceType t, Side s, int from, sbyte[] owners)
        {
            return new HashSet<int>(MoveRules.Generate(t, s, from, owners).Select(m => m.To));
        }

        private static int C(int x, int y) => BoardGraph.Cell(x, y);

        public static void MovementScenarios()
        {
            var o = EmptyBoard();
            // Officer: 4 orthogonal neighbours in camp.
            Check(Targets(PieceType.Colonel, Side.South, C(3, 1), o).SetEquals(new[] { C(3, 2), C(3, 0), C(2, 1), C(4, 1) }), "officer steps");
            // Officer at arm end: 3 camp neighbours + crossing.
            Check(Targets(PieceType.Major, Side.South, C(2, 3), o).SetEquals(new[] { C(2, 2), C(1, 3), C(3, 3), BoardGraph.LeftCrossing }), "arm end steps");
            // Officer at non-arm front cell cannot cross the band.
            Check(Targets(PieceType.Major, Side.South, C(3, 3), o).SetEquals(new[] { C(3, 2), C(2, 3), C(4, 3) }), "blocked by band");
            // From a crossing: the four arm ends, in both camps.
            Check(Targets(PieceType.Captain, Side.South, BoardGraph.LeftCrossing, o).SetEquals(new[] { C(0, 3), C(2, 3), C(0, 4), C(2, 4) }), "crossing exits");
            // Tank: forward two, blocked by a piece in between (no jumping).
            Check(Targets(PieceType.Tank, Side.South, C(3, 1), o).SetEquals(new[] { C(3, 2), C(3, 0), C(2, 1), C(4, 1), C(3, 3) }), "tank forward 2");
            var blocked = EmptyBoard(); blocked[C(3, 2)] = (sbyte)Side.North;
            Check(!Targets(PieceType.Tank, Side.South, C(3, 1), blocked).Contains(C(3, 3)), "tank cannot jump");
            Check(Targets(PieceType.Tank, Side.South, C(3, 1), blocked).Contains(C(3, 2)), "tank attacks adjacent");
            // North forward is decreasing y.
            Check(Targets(PieceType.Cavalry, Side.North, C(4, 6), o).Contains(C(4, 4)) && !Targets(PieceType.Cavalry, Side.North, C(4, 6), o).Contains(C(4, 8 - 8)), "north cavalry forward 2");
            // Charger: depth 2 on an arm column can go 2 forward into the crossing, but not through it.
            Check(Targets(PieceType.Cavalry, Side.South, C(0, 2), o).Contains(BoardGraph.LeftCrossing), "charger enters crossing");
            Check(!Targets(PieceType.Tank, Side.South, C(0, 3), o).Contains(C(0, 4)) && !Targets(PieceType.Tank, Side.South, C(0, 3), o).Contains(C(2, 4)), "charger stops at crossing");
            // Engineer: slides, stops at pieces, captures, stops in crossing.
            var eng = Targets(PieceType.Engineer, Side.South, C(0, 0), o);
            Check(eng.SetEquals(new[] { C(0, 1), C(0, 2), C(0, 3), BoardGraph.LeftCrossing, C(1, 0), C(2, 0), C(3, 0), C(4, 0), C(5, 0), C(6, 0), C(7, 0) }), "engineer slides");
            var mixed = EmptyBoard(); mixed[C(0, 2)] = (sbyte)Side.North; mixed[C(3, 0)] = (sbyte)Side.South;
            var eng2 = Targets(PieceType.Engineer, Side.South, C(0, 0), mixed);
            Check(eng2.SetEquals(new[] { C(0, 1), C(0, 2), C(1, 0), C(2, 0) }), "engineer captures/blocked");
            Check(Targets(PieceType.Engineer, Side.South, BoardGraph.RightCrossing, o).SetEquals(new[] { C(5, 3), C(7, 3), C(5, 4), C(7, 4) }), "engineer leaves crossing one step");
            // Airplane: whole column both ways (across the band), jumps, one step sideways, never a crossing.
            var air = EmptyBoard(); air[C(1, 3)] = (sbyte)Side.South; air[C(1, 4)] = (sbyte)Side.North; air[C(1, 6)] = (sbyte)Side.South;
            var fly = MoveRules.Generate(PieceType.Airplane, Side.South, C(1, 1), air);
            var flyTo = new HashSet<int>(fly.Select(m => m.To));
            Check(flyTo.SetEquals(new[] { C(1, 2), C(1, 4), C(1, 5), C(1, 7), C(1, 0), C(0, 1), C(2, 1) }), "airplane targets: " + string.Join(",", flyTo.Select(BoardGraph.Describe)));
            Check(fly.First(m => m.To == C(1, 5)).Jumped == 2, "airplane jump count");
            Check(fly.First(m => m.To == C(1, 4)).IsAttack, "airplane attacks across band");
            Check(!flyTo.Contains(BoardGraph.LeftCrossing), "airplane never stops in a crossing");
            // Immobile.
            Check(MoveRules.Generate(PieceType.Mine, Side.South, C(2, 1), o).Count == 0 && MoveRules.Generate(PieceType.Flag, Side.South, C(2, 1), o).Count == 0, "immobile");
            // Nobody except the airplane goes from one camp cell straight to the other camp.
            foreach (var t in PieceCatalog.AllTypes.Where(t => t != PieceType.Airplane))
                for (int n = 0; n < BoardGraph.CellCount; n++)
                    foreach (var m in MoveRules.Generate(t, Side.South, n, o))
                        if (BoardGraph.IsCell(m.To)) Check(BoardGraph.InCamp(n, Side.South) == BoardGraph.InCamp(m.To, Side.South), $"{t} jumped the band {n}->{m.To}");
        }

        public static void Formations()
        {
            int distinct = 0;
            foreach (FormationStyle style in Enum.GetValues(typeof(FormationStyle)))
            {
                var signatures = new HashSet<string>();
                foreach (var side in new[] { Side.South, Side.North })
                    for (int seed = 1; seed <= 150; seed++)
                    {
                        var f = FormationGenerator.Generate(side, style, seed);
                        PlacementRules.Validate(f);
                        Check(f.Count == 31, "31 placed");
                        Check(BoardGraph.CampCells(side).Count(n => !f.Pieces.ContainsKey(n)) == 1, "exactly one empty cell");
                        foreach (var p in f.Pieces)
                        {
                            Check(BoardGraph.InCamp(p.Key, side), "in camp");
                            if (p.Value == PieceType.Mine || p.Value == PieceType.Flag) Check(!BoardGraph.IsArmEnd(p.Key), "no mine/flag on gateway");
                            if (p.Value == PieceType.Flag) Check(BoardGraph.Depth(p.Key, side) > 0, "flag not on back row");
                        }
                        var again = FormationGenerator.Generate(side, style, seed);
                        Check(f.Signature() == again.Signature(), "same seed -> same formation");
                        if (side == Side.South) signatures.Add(f.Signature());
                    }
                Check(signatures.Count >= 140, $"{style}: seeds produce varied formations ({signatures.Count})");
                distinct += signatures.Count;
            }
            // Styles differ in intent: mean depth of officers.
            double MeanDepth(FormationStyle s, Func<PieceType, bool> filter)
            {
                double sum = 0; int n = 0;
                for (int seed = 1; seed <= 100; seed++)
                    foreach (var p in FormationGenerator.Generate(Side.South, s, seed).Pieces)
                        if (filter(p.Value)) { sum += BoardGraph.Depth(p.Key, Side.South); n++; }
                return sum / n;
            }
            double defGen = MeanDepth(FormationStyle.Defensive, t => t <= PieceType.MajorGeneral);
            double aggGen = MeanDepth(FormationStyle.Aggressive, t => t <= PieceType.MajorGeneral);
            double mobMob = MeanDepth(FormationStyle.Mobile, t => t == PieceType.Airplane || t == PieceType.Tank || t == PieceType.Engineer);
            double defMob = MeanDepth(FormationStyle.Defensive, t => t == PieceType.Airplane || t == PieceType.Tank || t == PieceType.Engineer);
            Check(aggGen > defGen + 1.0, $"aggressive generals further forward ({aggGen:0.00} vs {defGen:0.00})");
            Check(mobMob > defMob + 0.5, $"mobile style puts mobile pieces forward ({mobMob:0.00} vs {defMob:0.00})");
            Metrics.Add($"formation mean depth of generals: defensive {defGen:0.00}, aggressive {aggGen:0.00}; mobile units: mobile {mobMob:0.00}, defensive {defMob:0.00}");
        }

        public static void ManualEdits()
        {
            var f = FormationGenerator.Generate(Side.South, FormationStyle.Balanced, 7);
            int mine = f.Pieces.First(p => p.Value == PieceType.Mine).Key;
            int arm = BoardGraph.CampCells(Side.South).First(BoardGraph.IsArmEnd);
            string before = f.Signature();
            Check(!f.TryMoveOrSwap(mine, arm), "mine cannot move onto a gateway");
            Check(f.Signature() == before, "refused edit changes nothing");
            int flag = f.Pieces.First(p => p.Value == PieceType.Flag).Key;
            int back = BoardGraph.CampCell(Side.South, 0, 0);
            Check(!f.TryMoveOrSwap(flag, back), "flag cannot move to the back row");
            Check(!f.TryMoveOrSwap(flag, BoardGraph.Cell(0, 5)), "cannot place in enemy camp");
            // A legal swap between two ordinary pieces.
            var a = f.Pieces.First(p => p.Value == PieceType.General).Key;
            var b = f.Pieces.First(p => p.Value == PieceType.SecondLieutenant).Key;
            Check(f.TryMoveOrSwap(a, b), "swap ok");
            Check(f.Pieces[a] == PieceType.SecondLieutenant && f.Pieces[b] == PieceType.General, "swapped");
            // Swapping a mine into a gateway via the other piece is refused too.
            int armPiece = BoardGraph.CampCells(Side.South).First(n => BoardGraph.IsArmEnd(n) && f.Pieces.ContainsKey(n));
            Check(!f.TryMoveOrSwap(armPiece, mine), "swap that would put mine on gateway refused") ;
            int empty = f.EmptyCell();
            var mover = f.Pieces.First(p => p.Value == PieceType.Captain).Key;
            Check(f.TryMoveOrSwap(mover, empty), "move into empty cell");
            Check(f.EmptyCell() == mover, "empty cell follows");
            PlacementRules.Validate(f);
        }
    }
}
