using System;

namespace MilitaryShogi.Cpu
{
    /// <summary>
    /// Applies the referee's stalemate intervention (Observation.RepetitionReferee) to a finished
    /// decision. It does not change how the CPU thinks: all candidates keep the scores the brain
    /// gave them. If the CPU's chosen move would re-enter the repetition the referee intervened in,
    /// the highest-scored candidate that does not is played instead (the next-best legal move). If
    /// every legal move continues the repetition, the original choice stays.
    /// </summary>
    public static class RefereeChoice
    {
        public const string NotePrefix = "審判介入：同じ局面の反復のため、反復を続けない次善の手に変更";

        /// <returns>True if the choice was changed.</returns>
        public static bool Apply(DecisionReport report, Func<CandidateMove, bool> continuesRepetition)
        {
            if (report == null || report.Chosen == null || !continuesRepetition(report.Chosen)) return false;
            foreach (var c in report.Candidates)          // sorted by score, best first
            {
                if (continuesRepetition(c)) continue;
                var original = report.Chosen;
                report.Chosen = c;
                report.Reason = NotePrefix + "（本来の手 C#" + original.PieceNumber + "）。" + report.Reason;
                return true;
            }
            return false;
        }
    }
}
