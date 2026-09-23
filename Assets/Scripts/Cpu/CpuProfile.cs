using System;
using MilitaryShogi.Rules;

namespace MilitaryShogi.Cpu
{
    /// <summary>CPUの強さ. Differences are in decision precision only; knowledge/inference is identical.</summary>
    public enum CpuStrength { Weak = 0, Normal = 1, Strong = 2 }

    /// <summary>
    /// What the player chooses before a game in play mode: strength and temperament.
    /// Temperament is -2 (かなり防御的) … +2 (かなり攻撃的). Neither setting changes the rules,
    /// the observations or the belief update: they only change which risks the CPU accepts
    /// and how precisely it picks among its evaluated moves.
    /// </summary>
    public struct CpuProfile
    {
        public CpuStrength Strength;
        public int Temperament;

        public CpuProfile(CpuStrength strength, int temperament)
        {
            Strength = strength;
            Temperament = Math.Max(-2, Math.Min(2, temperament));
        }

        /// <summary>中・バランス. With the same formation style its decisions equal 1.0.0's CPU.</summary>
        public static CpuProfile Standard { get { return new CpuProfile(CpuStrength.Normal, 0); } }

        public static string StrengthName(CpuStrength s)
        {
            return s == CpuStrength.Weak ? "弱" : s == CpuStrength.Strong ? "強" : "中";
        }

        public static string TemperamentName(int t)
        {
            switch (t)
            {
                case 2: return "かなり攻撃的";
                case 1: return "攻撃的";
                case -1: return "防御的";
                case -2: return "かなり防御的";
                default: return "バランス";
            }
        }

        // Relative weights of the five formation styles per temperament.
        // Order: Defensive, Aggressive, Balanced, Trap, Mobile.
        private static readonly double[][] styleWeights =
        {
            new[] { 0.45, 0.00, 0.15, 0.40, 0.00 },   // -2
            new[] { 0.30, 0.00, 0.30, 0.30, 0.10 },   // -1
            new[] { 0.15, 0.15, 0.40, 0.15, 0.15 },   //  0
            new[] { 0.00, 0.30, 0.30, 0.10, 0.30 },   // +1
            new[] { 0.00, 0.45, 0.15, 0.00, 0.40 },   // +2
        };

        public static double StyleWeight(int temperament, FormationStyle style)
        {
            return styleWeights[Math.Max(-2, Math.Min(2, temperament)) + 2][(int)style];
        }

        /// <summary>
        /// Formation style for a temperament: offensive temperaments favour ガンガン行こうぜ！/きどうせんだ！,
        /// defensive ones いのちだいじに/わなにはめろ, balanced includes all with じっくりいこうぜ most likely.
        /// Still drawn with the formation seed, so the seed keeps some variety.
        /// </summary>
        public static FormationStyle ChooseStyle(int formationSeed, int temperament)
        {
            var w = styleWeights[Math.Max(-2, Math.Min(2, temperament)) + 2];
            double r = DeterministicRandom.Derive(formationSeed, 0x57E, temperament + 10).NextDouble();
            double sum = 0;
            for (int i = 0; i < w.Length; i++) sum += w[i];
            r *= sum;
            for (int i = 0; i < w.Length; i++)
            {
                r -= w[i];
                if (r < 0 && w[i] > 0) return (FormationStyle)i;
            }
            return FormationStyle.Balanced;
        }

        /// <summary>
        /// Personality = formation style personality (as in 1.0.0) × temperament × strength.
        /// Temperament +: more progress and attack opportunity, less defence/risk aversion.
        /// Temperament −: the opposite (HQ defence, keeping valuable pieces, avoiding exposure).
        /// </summary>
        public CpuPersonality Personality(FormationStyle style)
        {
            var p = CpuPersonality.For(style);
            int t = Temperament;
            p.ProgressWeight *= 1 + 0.15 * t;
            p.OpportunityWeight *= 1 + 0.12 * t;
            p.DefenseWeight *= 1 - 0.12 * t;
            p.RiskAversion *= 1 - 0.12 * t;
            switch (Strength)
            {
                case CpuStrength.Weak:
                    // Evaluates the current position with full knowledge but does not read replies
                    // and picks more loosely among its best few moves.
                    p.LookaheadEnabled = false;
                    p.NearMargin = 0.5;
                    p.Temperature = 0.2;
                    p.MaxNearCandidates = 6;
                    break;
                case CpuStrength.Strong:
                    // Same 2-ply read; tighter choice and more weight on information from probes.
                    p.NearMargin = 0.05;
                    p.Temperature = 0.03;
                    p.InformationWeight *= 1.25;
                    break;
            }
            return p;
        }
    }
}
