using System.Collections.Generic;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;

namespace MilitaryShogi.Cpu
{
    /// <summary>Per-term breakdown of a candidate's score (change versus the current position).</summary>
    public struct ScoreTerms
    {
        public double Material;     // expected piece value balance (enemy values from beliefs)
        public double Progress;     // officers approaching the enemy headquarters
        public double Defense;      // danger to our headquarters (incl. immediate capture threats)
        public double Safety;       // expected loss from enemy attacks next turn
        public double Opportunity;  // good attacks available next turn
        public double Information;  // expected entropy reduction from a probing attack
        public double Repetition;   // penalty for shuffling back and forth
        public double Victory;      // capturing the enemy headquarters
        public double Lookahead;    // correction from reading the opponent's best reply (2-ply)

        public double Total { get { return Material + Progress + Defense + Safety + Opportunity + Information + Repetition + Victory + Lookahead; } }

        public static ScoreTerms operator -(ScoreTerms a, ScoreTerms b)
        {
            return new ScoreTerms
            {
                Material = a.Material - b.Material, Progress = a.Progress - b.Progress, Defense = a.Defense - b.Defense,
                Safety = a.Safety - b.Safety, Opportunity = a.Opportunity - b.Opportunity, Information = a.Information - b.Information,
                Repetition = a.Repetition - b.Repetition, Victory = a.Victory - b.Victory, Lookahead = a.Lookahead - b.Lookahead,
            };
        }

        public static ScoreTerms operator +(ScoreTerms a, ScoreTerms b)
        {
            return new ScoreTerms
            {
                Material = a.Material + b.Material, Progress = a.Progress + b.Progress, Defense = a.Defense + b.Defense,
                Safety = a.Safety + b.Safety, Opportunity = a.Opportunity + b.Opportunity, Information = a.Information + b.Information,
                Repetition = a.Repetition + b.Repetition, Victory = a.Victory + b.Victory, Lookahead = a.Lookahead + b.Lookahead,
            };
        }

        public static ScoreTerms operator *(double k, ScoreTerms a)
        {
            return new ScoreTerms
            {
                Material = k * a.Material, Progress = k * a.Progress, Defense = k * a.Defense, Safety = k * a.Safety,
                Opportunity = k * a.Opportunity, Information = k * a.Information, Repetition = k * a.Repetition, Victory = k * a.Victory,
                Lookahead = k * a.Lookahead,
            };
        }

        public IEnumerable<KeyValuePair<string, double>> Named()
        {
            yield return new KeyValuePair<string, double>("駒得", Material);
            yield return new KeyValuePair<string, double>("前進", Progress);
            yield return new KeyValuePair<string, double>("司令部防衛", Defense);
            yield return new KeyValuePair<string, double>("被攻撃リスク", Safety);
            yield return new KeyValuePair<string, double>("攻撃機会", Opportunity);
            yield return new KeyValuePair<string, double>("情報価値", Information);
            yield return new KeyValuePair<string, double>("反復抑制", Repetition);
            yield return new KeyValuePair<string, double>("勝利", Victory);
            yield return new KeyValuePair<string, double>("先読み", Lookahead);
        }
    }

    public sealed class CandidateMove
    {
        public MoveCommand Command;
        public int From;
        public int PieceNumber;
        public PieceType PieceType;       // CPU's own piece (CPU knows its own kinds)
        public int TargetEnemyId = -1;
        public int TargetEnemyNumber;
        public double WinP, LoseP, TieP;  // for attacks
        public ScoreTerms Terms;
        public double Score;
        public string Note;
        public string ReplyNote;          // opponent's most damaging reply found by the 2-ply read
    }

    public sealed class EnemyBeliefSnapshot
    {
        public int Id, Number, Node;
        public bool Alive;
        public double[] Probability;
        public bool[] Allowed;
        public List<BeliefNote> Notes;
        public double Entropy;
    }

    /// <summary>Everything the thinking monitor shows for one CPU decision.</summary>
    public sealed class DecisionReport
    {
        public int Ply;                  // the ply this decision will be (1-based)
        public Side Side;
        public FormationStyle Style;
        public int DecisionSeed;
        public List<CandidateMove> Candidates = new List<CandidateMove>();
        public CandidateMove Chosen;
        public string Reason;
        public List<string> Considerations = new List<string>();
        public List<EnemyBeliefSnapshot> Beliefs = new List<EnemyBeliefSnapshot>();
        /// <summary>The CPU's own pieces at decision time (the CPU knows its own kinds).</summary>
        public IReadOnlyList<OwnPieceView> Own;
        public double ThinkMilliseconds;
    }
}
