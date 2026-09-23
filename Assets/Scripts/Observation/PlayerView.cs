using System.Collections.Generic;
using MilitaryShogi.Rules;

namespace MilitaryShogi.Observation
{
    // ------------------------------------------------------------------------
    // Information boundary.
    //
    // Everything a player (human UI or CPU) is allowed to know is expressed with
    // the types in this assembly. Enemy pieces are EnemyPieceView, which has NO
    // field for a piece kind: the true kind of an enemy cannot be represented
    // here at all. Views are immutable copies built by the Engine; they hold no
    // references back into the authoritative state.
    // ------------------------------------------------------------------------

    public enum GameStatus { Setup, Playing, Finished }

    /// <summary>
    /// Why the game ended. NoCapturers (1.3.0): neither side has 大将〜少佐 left, so no side can
    /// capture the headquarters. Resigned (1.3.0): the loser resigned (only the player can resign).
    /// </summary>
    public enum EndReason { None, HeadquartersCaptured, NoLegalMoves, MoveLimit, NoCapturers, Resigned }

    /// <summary>A piece of the viewing player: kind is known (it is your own piece).</summary>
    public sealed class OwnPieceView
    {
        public readonly int Id;
        public readonly int Number;        // 1..31 as displayed to the owner
        public readonly PieceType Type;
        public readonly int Node;          // -1 when removed
        public readonly int InitialNode;
        public bool Alive { get { return Node >= 0; } }

        public OwnPieceView(int id, int number, PieceType type, int node, int initialNode)
        { Id = id; Number = number; Type = type; Node = node; InitialNode = initialNode; }
    }

    /// <summary>An opponent piece: identity by id and position only.</summary>
    public sealed class EnemyPieceView
    {
        public readonly int Id;
        public readonly int Number;        // 1..31, "Enemy #n"
        public readonly int Node;          // -1 when removed
        public readonly int InitialNode;
        public bool Alive { get { return Node >= 0; } }

        public EnemyPieceView(int id, int number, int node, int initialNode)
        { Id = id; Number = number; Node = node; InitialNode = initialNode; }
    }

    /// <summary>Combat as seen by both players: who met whom and the outcome. Never the kinds.</summary>
    public sealed class ObservedCombat
    {
        public readonly int AttackerId;
        public readonly int DefenderId;
        public readonly CombatOutcome Outcome;

        public ObservedCombat(int attackerId, int defenderId, CombatOutcome outcome)
        { AttackerId = attackerId; DefenderId = defenderId; Outcome = outcome; }
    }

    /// <summary>
    /// One ply as seen by everyone at the table: which piece moved, where, along
    /// which path, how many pieces it flew over, the board occupancy (sides only)
    /// just before the move, and the combat outcome if any.
    /// </summary>
    public sealed class ObservedMove
    {
        public readonly int Ply;
        public readonly Side Mover;
        public readonly int PieceId;
        public readonly int From;
        public readonly int To;
        public readonly int[] Path;
        public readonly int Jumped;
        public readonly sbyte[] OwnersBefore;
        public readonly ObservedCombat Combat;   // null for a plain move

        public ObservedMove(int ply, Side mover, int pieceId, int from, int to, int[] path, int jumped, sbyte[] ownersBefore, ObservedCombat combat)
        {
            Ply = ply; Mover = mover; PieceId = pieceId; From = from; To = to;
            Path = (int[])path.Clone(); Jumped = jumped; OwnersBefore = (sbyte[])ownersBefore.Clone(); Combat = combat;
        }
    }

    public sealed class PlayerView
    {
        public readonly Side Me;
        public readonly int Ply;                 // number of plies already played
        public readonly Side ToMove;
        public readonly GameStatus Status;
        public readonly Side? Winner;            // null = draw or not finished
        public readonly EndReason EndReason;
        public readonly IReadOnlyList<OwnPieceView> Own;
        public readonly IReadOnlyList<EnemyPieceView> Enemy;
        public readonly IReadOnlyList<ObservedMove> History;
        public readonly sbyte[] Owners;          // current occupancy by side, per node

        public PlayerView(Side me, int ply, Side toMove, GameStatus status, Side? winner, EndReason endReason,
            IReadOnlyList<OwnPieceView> own, IReadOnlyList<EnemyPieceView> enemy, IReadOnlyList<ObservedMove> history, sbyte[] owners)
        {
            Me = me; Ply = ply; ToMove = toMove; Status = status; Winner = winner; EndReason = endReason;
            Own = own; Enemy = enemy; History = history; Owners = (sbyte[])owners.Clone();
        }

        public OwnPieceView OwnById(int id)
        {
            foreach (var p in Own) if (p.Id == id) return p;
            return null;
        }

        public EnemyPieceView EnemyById(int id)
        {
            foreach (var p in Enemy) if (p.Id == id) return p;
            return null;
        }

        public bool IsOwnId(int id) { return OwnById(id) != null; }
    }

    /// <summary>What a player submits: move piece <see cref="PieceId"/> to node <see cref="To"/>.</summary>
    public struct MoveCommand
    {
        public int PieceId;
        public int To;
        public MoveCommand(int pieceId, int to) { PieceId = pieceId; To = to; }
        public override string ToString() { return "#" + PieceId + "->" + BoardGraph.Describe(To); }
    }
}
