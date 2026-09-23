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
            brain = new CpuBrain(side, decisionSeed, CpuPersonality.For(Style));
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
            Observe(view);
            var report = brain.Decide(view, Knowledge, Style);
            Reports.Add(report);
            return report;
        }
    }
}
