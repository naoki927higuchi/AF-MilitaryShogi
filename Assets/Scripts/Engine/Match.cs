using System;
using System.Collections.Generic;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;

namespace MilitaryShogi.Engine
{
    public sealed class MatchConfig
    {
        /// <summary>Draw when this many plies have been played in total.</summary>
        public int MaxPlies = 800;
        /// <summary>Draw when this many consecutive plies pass without any combat.</summary>
        public int MaxPliesWithoutCombat = 160;
    }

    /// <summary>The single piece record in the authoritative state. Never leaves this assembly.</summary>
    internal sealed class PieceState
    {
        public int Id;
        public int Number;
        public Side Side;
        public PieceType Type;
        public int Node;
        public int InitialNode;
    }

    /// <summary>
    /// The only place where the true kind of every piece is stored. Internal: code in
    /// other assemblies (in particular MilitaryShogi.Cpu, which does not even reference
    /// this assembly) cannot reach it.
    /// </summary>
    internal sealed class AuthoritativeGameState
    {
        public readonly PieceState[] Pieces;
        public readonly int[] PieceAt = new int[BoardGraph.NodeCount];   // piece id or -1
        public readonly sbyte[] Owners = new sbyte[BoardGraph.NodeCount];
        public readonly List<ObservedMove> History = new List<ObservedMove>();
        public Side ToMove = Side.South;
        public int Ply;
        public int LastCombatPly;
        public GameStatus Status = GameStatus.Playing;
        public Side? Winner;
        public EndReason EndReason = EndReason.None;

        public AuthoritativeGameState(Formation south, Formation north)
        {
            var list = new List<PieceState>();
            for (int i = 0; i < PieceAt.Length; i++) { PieceAt[i] = -1; Owners[i] = MoveRules.Empty; }
            foreach (var f in new[] { south, north })
            {
                int number = 1;
                // Ids and numbers follow board position only, so they reveal nothing about kinds.
                foreach (int node in BoardGraph.CampCells(f.Side))
                {
                    PieceType type;
                    if (!f.TryGet(node, out type)) continue;
                    var p = new PieceState { Id = list.Count, Number = number++, Side = f.Side, Type = type, Node = node, InitialNode = node };
                    list.Add(p);
                    PieceAt[node] = p.Id;
                    Owners[node] = (sbyte)f.Side;
                }
            }
            Pieces = list.ToArray();
        }
    }

    /// <summary>
    /// Referee for one game. Holds the authoritative state, validates and applies moves
    /// (the Judge), and hands each player a <see cref="PlayerView"/> built by
    /// <see cref="ObservationBuilder"/>. Nothing public on this class returns a piece
    /// kind of the opponent while the game is in progress.
    /// </summary>
    public sealed class Match
    {
        private readonly AuthoritativeGameState state;
        private readonly MatchConfig config;

        public Match(Formation south, Formation north, MatchConfig config = null)
        {
            if (south.Side != Side.South || north.Side != Side.North) throw new ArgumentException("formation sides");
            PlacementRules.Validate(south);
            PlacementRules.Validate(north);
            this.config = config ?? new MatchConfig();
            state = new AuthoritativeGameState(south, north);
        }

        public Side ToMove { get { return state.ToMove; } }
        public int Ply { get { return state.Ply; } }
        public GameStatus Status { get { return state.Status; } }
        public Side? Winner { get { return state.Winner; } }
        public EndReason EndReason { get { return state.EndReason; } }
        public IReadOnlyList<ObservedMove> History { get { return state.History; } }

        public PlayerView GetView(Side side) { return ObservationBuilder.Build(state, side); }

        /// <summary>Legal moves of the side to move, as commands (no kinds).</summary>
        public List<MoveCommand> LegalMoves(Side side)
        {
            var result = new List<MoveCommand>();
            if (state.Status != GameStatus.Playing) return result;
            var buffer = new List<MoveTarget>();
            foreach (var p in state.Pieces)
            {
                if (p.Side != side || p.Node < 0) continue;
                buffer.Clear();
                MoveRules.Generate(PieceCatalog.MoveClassOf(p.Type), side, p.Node, state.Owners, buffer);
                foreach (var t in buffer) result.Add(new MoveCommand(p.Id, t.To));
            }
            return result;
        }

        /// <summary>Validate and apply a move for <paramref name="side"/>. Returns the public record of the ply.</summary>
        public ObservedMove Apply(Side side, MoveCommand command)
        {
            if (state.Status != GameStatus.Playing) throw new InvalidOperationException("The game is over.");
            if (side != state.ToMove) throw new InvalidOperationException("Not " + side + "'s turn.");
            if (command.PieceId < 0 || command.PieceId >= state.Pieces.Length) throw new ArgumentException("Unknown piece.");
            var piece = state.Pieces[command.PieceId];
            if (piece.Side != side || piece.Node < 0) throw new ArgumentException("That piece cannot be moved by " + side + ".");

            MoveTarget? chosen = null;
            foreach (var t in MoveRules.Generate(piece.Type, side, piece.Node, state.Owners))
                if (t.To == command.To) { chosen = t; break; }
            if (chosen == null) throw new ArgumentException("Illegal move " + command + ".");

            var target = chosen.Value;
            var ownersBefore = (sbyte[])state.Owners.Clone();
            int from = piece.Node;
            ObservedCombat combat = null;
            state.Ply++;

            int defenderId = state.PieceAt[target.To];
            Vacate(piece);
            if (defenderId < 0)
            {
                Occupy(piece, target.To);
            }
            else
            {
                var defender = state.Pieces[defenderId];
                var outcome = Judge.Resolve(state, piece, defender);
                combat = new ObservedCombat(piece.Id, defender.Id, outcome);
                state.LastCombatPly = state.Ply;
                switch (outcome)
                {
                    case CombatOutcome.AttackerWins:
                        Remove(defender);
                        Occupy(piece, target.To);
                        break;
                    case CombatOutcome.DefenderWins:
                        piece.Node = -1;
                        break;
                    default:
                        Remove(defender);
                        piece.Node = -1;
                        break;
                }
            }

            var record = new ObservedMove(state.Ply, side, piece.Id, from, target.To, target.Path, target.Jumped, ownersBefore, combat);
            state.History.Add(record);
            state.ToMove = side.Opponent();
            UpdateStatus(piece);
            return record;
        }

        private void UpdateStatus(PieceState mover)
        {
            if (mover.Node >= 0 && BoardGraph.IsHeadquarters(mover.Node, mover.Side.Opponent()) && PieceCatalog.CanCaptureHeadquarters(mover.Type))
            {
                Finish(mover.Side, EndReason.HeadquartersCaptured);
                return;
            }
            if (LegalMoves(state.ToMove).Count == 0)
            {
                Finish(state.ToMove.Opponent(), EndReason.NoLegalMoves);
                return;
            }
            if (state.Ply >= config.MaxPlies || state.Ply - state.LastCombatPly >= config.MaxPliesWithoutCombat)
                Finish(null, EndReason.MoveLimit);
        }

        private void Finish(Side? winner, EndReason reason)
        {
            state.Status = GameStatus.Finished;
            state.Winner = winner;
            state.EndReason = reason;
        }

        private void Vacate(PieceState p)
        {
            state.PieceAt[p.Node] = -1;
            state.Owners[p.Node] = MoveRules.Empty;
        }

        private void Occupy(PieceState p, int node)
        {
            p.Node = node;
            state.PieceAt[node] = p.Id;
            state.Owners[node] = (sbyte)p.Side;
        }

        private void Remove(PieceState p)
        {
            Vacate(p);
            p.Node = -1;
        }

        /// <summary>
        /// True kinds, only after the game has finished (for post-game analysis in tests).
        /// Throws while the game is running.
        /// </summary>
        public PieceType RevealAfterGameEnd(int pieceId)
        {
            if (state.Status != GameStatus.Finished) throw new InvalidOperationException("Piece kinds stay hidden until the game ends.");
            return state.Pieces[pieceId].Type;
        }
    }

    /// <summary>審判: resolves a fight using the combat table and the flag rule.</summary>
    internal static class Judge
    {
        public static CombatOutcome Resolve(AuthoritativeGameState state, PieceState attacker, PieceState defender)
        {
            return CombatTable.Resolve(attacker.Type, EffectiveStrength(state, defender));
        }

        /// <summary>The kind a piece fights as; null for an unbacked flag (loses to all).</summary>
        public static PieceType? EffectiveStrength(AuthoritativeGameState state, PieceState p)
        {
            if (p.Type != PieceType.Flag) return p.Type;
            int back = FlagRule.BackingCell(p.Node, p.Side);
            if (back < 0) return null;
            int id = state.PieceAt[back];
            if (id < 0 || state.Pieces[id].Side != p.Side) return null;
            return state.Pieces[id].Type;
        }
    }

    /// <summary>Builds the per-player view: own kinds, enemy ids/positions, public history.</summary>
    internal static class ObservationBuilder
    {
        public static PlayerView Build(AuthoritativeGameState state, Side side)
        {
            var own = new List<OwnPieceView>();
            var enemy = new List<EnemyPieceView>();
            foreach (var p in state.Pieces)
            {
                if (p.Side == side) own.Add(new OwnPieceView(p.Id, p.Number, p.Type, p.Node, p.InitialNode));
                else enemy.Add(new EnemyPieceView(p.Id, p.Number, p.Node, p.InitialNode));
            }
            return new PlayerView(side, state.Ply, state.ToMove, state.Status, state.Winner, state.EndReason,
                own, enemy, state.History.ToArray(), state.Owners);
        }
    }
}
