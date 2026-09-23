using System;
using System.Collections.Generic;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;

namespace MilitaryShogi.Cpu
{
    /// <summary>
    /// Facade used by the game. The CPU receives nothing but <see cref="PlayerView"/>s
    /// (this assembly does not reference the engine assembly, so it cannot touch the
    /// referee's state) and answers with a <see cref="MoveCommand"/>.
    /// </summary>
    public sealed class CpuPlayer
    {
        public readonly Side Side;
        public readonly int FormationSeed;
        public readonly int DecisionSeed;
        public readonly FormationStyle Style;
        public readonly bool StyleForced;
        /// <summary>Strength/temperament chosen by the player; null for the 1.0.0 construction path.</summary>
        public readonly CpuProfile? Profile;
        public readonly CpuPersonality Personality;
        public CpuKnowledge Knowledge { get; private set; }
        public readonly List<DecisionReport> Reports = new List<DecisionReport>();
        private readonly CpuBrain brain;

        /// <param name="forcedStyle">null = derive the style from the formation seed (normal play).</param>
        public CpuPlayer(Side side, int formationSeed, int decisionSeed, FormationStyle? forcedStyle = null)
        {
            Side = side;
            FormationSeed = formationSeed;
            DecisionSeed = decisionSeed;
            StyleForced = forcedStyle.HasValue;
            Style = forcedStyle ?? FormationStyles.FromSeed(formationSeed);
            Personality = CpuPersonality.For(Style);
            brain = new CpuBrain(side, decisionSeed, Personality);
        }

        /// <summary>
        /// Play-mode construction. The formation style is drawn from the formation seed weighted by
        /// the temperament (unless forced for research), and the same temperament also shapes the
        /// in-game evaluation, so placement and play agree. Seeds stay separate as in 1.0.0.
        /// </summary>
        public CpuPlayer(Side side, int formationSeed, int decisionSeed, CpuProfile profile, FormationStyle? forcedStyle = null)
        {
            Side = side;
            FormationSeed = formationSeed;
            DecisionSeed = decisionSeed;
            Profile = profile;
            StyleForced = forcedStyle.HasValue;
            Style = forcedStyle ?? CpuProfile.ChooseStyle(formationSeed, profile.Temperament);
            Personality = profile.Personality(Style);
            brain = new CpuBrain(side, decisionSeed, Personality);
        }

        /// <summary>Initial placement: style template + formation-seed noise. Deterministic.</summary>
        public Formation CreateFormation()
        {
            return FormationGenerator.Generate(Side, Style, FormationSeed);
        }

        /// <summary>Feed the latest view (after any ply) so the knowledge stays current.</summary>
        public void Observe(PlayerView view)
        {
            if (view.Me != Side) throw new ArgumentException("This view belongs to the other player.");
            if (Knowledge == null) Knowledge = new CpuKnowledge(view);
            Knowledge.Update(view);
        }

        public DecisionReport Decide(PlayerView view)
        {
            var report = Think(view);
            Record(report);
            return report;
        }

        /// <summary>
        /// Computes a decision without recording it. Once the view has been observed this changes
        /// no CPU state, so it may run in the background and be discarded or applied later.
        /// </summary>
        public DecisionReport Think(PlayerView view)
        {
            Observe(view);
            return brain.Decide(view, Knowledge, Style);
        }

        /// <summary>Adds a decision to <see cref="Reports"/> when its move is actually played.</summary>
        public void Record(DecisionReport report) { Reports.Add(report); }
    }
}
