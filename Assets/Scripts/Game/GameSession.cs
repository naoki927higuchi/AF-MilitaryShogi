using System.Collections.Generic;
using System.Linq;
using System.Text;
using MilitaryShogi.Cpu;
using MilitaryShogi.Engine;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;

namespace MilitaryShogi.Game
{
    /// <summary>Seeds and CPU choices for one game (shared by both presentation modes).</summary>
    public sealed class GameSettings
    {
        public int PlayerFormationSeed = 1001;
        public int CpuFormationSeed = 2002;
        public int CpuDecisionSeed = 3003;
        public CpuStrength Strength = CpuStrength.Normal;   // 中
        public int Temperament;                              // バランス
        public FormationStyle? CpuStyle;                     // research only: force a formation style
        public bool EffectsOn = true;
        public float EffectSpeed = 1f;
        public bool ShowEnemyNumbers = true;

        public CpuProfile Profile { get { return new CpuProfile(Strength, Temperament); } }
    }

    /// <summary>A combat line as the human remembers it: own kind, enemy number, result. Never the enemy kind.</summary>
    public sealed class CombatRecord
    {
        public int Ply;
        public bool PlayerAttacked;
        public PieceType OwnType;
        public int OwnNumber;
        public int EnemyNumber;
        public CombatOutcome Outcome;          // from the attacker's point of view
        public bool PlayerWon { get { return PlayerAttacked ? Outcome == CombatOutcome.AttackerWins : Outcome == CombatOutcome.DefenderWins; } }
        public bool Tie { get { return Outcome == CombatOutcome.Tie; } }
    }

    /// <summary>
    /// Everything that makes up a game: settings/seeds, the referee (game state), the CPU with its
    /// knowledge, the human's view and the battle history. The presentation mode (対戦/研究) is not
    /// part of it – switching modes never touches this object.
    /// </summary>
    public sealed class GameSession
    {
        public const Side Human = Side.South;
        public const Side Computer = Side.North;

        public readonly GameSettings Settings;
        public Match Match { get; private set; }
        public CpuPlayer Cpu { get; private set; }
        public PlayerView View { get; private set; }
        public Formation PlayerFormation { get; private set; }
        public FormationStyle PlayerStyle { get; private set; }
        public Formation CpuFormation { get; private set; }
        public readonly List<CombatRecord> Combats = new List<CombatRecord>();

        public GameSession(GameSettings settings)
        {
            Settings = settings;
            CreateCpu();
            AutoArrange();
        }

        public void CreateCpu()
        {
            Cpu = new CpuPlayer(Computer, Settings.CpuFormationSeed, Settings.CpuDecisionSeed, Settings.Profile, Settings.CpuStyle);
            CpuFormation = Cpu.CreateFormation();
        }

        public void AutoArrange()
        {
            PlayerStyle = FormationStyles.FromSeed(Settings.PlayerFormationSeed);
            PlayerFormation = FormationGenerator.Generate(Human, PlayerStyle, Settings.PlayerFormationSeed);
        }

        public bool Started { get { return Match != null; } }

        public void Start()
        {
            PlacementRules.Validate(PlayerFormation);
            Match = new Match(PlayerFormation.Clone(), CpuFormation);
            View = Match.GetView(Human);
            Cpu.Observe(Match.GetView(Computer));
        }

        /// <summary>Apply a move and refresh the views. Returns the public record and the human's view before it.</summary>
        public ObservedMove Apply(Side side, MoveCommand command, out PlayerView before)
        {
            before = View;
            var record = Match.Apply(side, command);
            View = Match.GetView(Human);
            Cpu.Observe(Match.GetView(Computer));
            if (record.Combat != null) Combats.Add(ToCombatRecord(record, before));
            return record;
        }

        private static CombatRecord ToCombatRecord(ObservedMove r, PlayerView before)
        {
            bool playerAttacked = r.Mover == Human;
            int ownId = playerAttacked ? r.Combat.AttackerId : r.Combat.DefenderId;
            int enemyId = playerAttacked ? r.Combat.DefenderId : r.Combat.AttackerId;
            var own = before.OwnById(ownId);
            return new CombatRecord
            {
                Ply = r.Ply, PlayerAttacked = playerAttacked, OwnType = own.Type, OwnNumber = own.Number,
                EnemyNumber = before.EnemyById(enemyId).Number, Outcome = r.Combat.Outcome,
            };
        }

        public PlayerView CpuView() { return Match.GetView(Computer); }

        /// <summary>
        /// Enemy pieces in the order they were removed (public information only: which piece left
        /// the board in which combat). Kinds are never involved.
        /// </summary>
        public static List<int> EnemyDeathOrder(PlayerView view)
        {
            var order = new List<int>();
            foreach (var m in view.History)
            {
                if (m.Combat == null) continue;
                var c = m.Combat;
                bool attackerRemoved = c.Outcome != CombatOutcome.AttackerWins;
                bool defenderRemoved = c.Outcome != CombatOutcome.DefenderWins;
                if (attackerRemoved && view.EnemyById(c.AttackerId) != null) order.Add(c.AttackerId);
                if (defenderRemoved && view.EnemyById(c.DefenderId) != null) order.Add(c.DefenderId);
            }
            return order;
        }

        /// <summary>
        /// Digest of the whole game state as the session sees it (history, human view, CPU knowledge
        /// and decisions, seeds). Used to prove that presentation changes leave the game untouched.
        /// </summary>
        public string Fingerprint()
        {
            var sb = new StringBuilder();
            sb.Append(Settings.PlayerFormationSeed).Append('/').Append(Settings.CpuFormationSeed).Append('/').Append(Settings.CpuDecisionSeed)
              .Append('/').Append(Settings.Strength).Append('/').Append(Settings.Temperament).Append('/').Append(Cpu.Style).Append('|');
            sb.Append(PlayerFormation.Signature()).Append('|').Append(CpuFormation.Signature()).Append('|');
            if (Match != null)
            {
                sb.Append(Match.Ply).Append(Match.ToMove).Append(Match.Status).Append('|');
                foreach (var m in Match.History) sb.Append(m.PieceId).Append('>').Append(m.To).Append(m.Combat != null ? m.Combat.Outcome.ToString() : "").Append(';');
                sb.Append('|').Append(string.Concat(View.Owners.Select(o => (char)('A' + o + 1))));
            }
            if (Cpu.Knowledge != null)
                foreach (var b in Cpu.Knowledge.Enemies) sb.Append('|').Append(string.Join(",", b.Probability.Select(p => p.ToString("R"))));
            foreach (var r in Cpu.Reports) sb.Append('#').Append(r.Chosen.Command).Append('=').Append(r.Chosen.Score.ToString("R"));
            sb.Append("|combats=").Append(Combats.Count);
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return System.BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 16);
        }
    }
}
