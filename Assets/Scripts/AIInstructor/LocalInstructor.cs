using MedSimXR.Assessment;
using MedSimXR.Equipment;
using MedSimXR.Training;

namespace MedSimXR.AIInstructor
{
    /// <summary>
    /// Deterministic instructor. It only repeats approved scenario data (questions, hints, explanations,
    /// equipment descriptions) and says so plainly when nothing verified is available.
    /// It does not score and does not invent clinical content.
    /// </summary>
    public class LocalInstructor
    {
        public const string NoInfo = "I do not have verified training information about that.";
        readonly EquipmentRegistry _equipment;

        public LocalInstructor(EquipmentRegistry equipment) { _equipment = equipment; }

        public string TaskInstruction(QuizQuestion q, int index, int total)
        {
            return "Task " + (index + 1) + " of " + total + ": " + q.prompt;
        }

        public string Explain(string objectId)
        {
            EquipmentEntry e;
            if (!_equipment.TryGet(objectId, out e)) return NoInfo;
            string s = e.displayName + ". " + e.description;
            if (!string.IsNullOrEmpty(e.trainingUse)) s += "\nTraining use: " + e.trainingUse;
            if (e.IsPlaceholder) s += "\n(Placeholder model - not a real equipment asset.)";
            return s;
        }

        public string AfterCorrect(QuizQuestion q)
        {
            return "Correct. " + (string.IsNullOrEmpty(q.explanation) ? "" : q.explanation);
        }

        public string AfterWrong(string selectedId, int attemptsLeft)
        {
            string name = _equipment.NameOf(selectedId);
            return "That is the " + name + ", not the item requested. " + attemptsLeft + " attempt(s) left. Press H for a hint.";
        }

        public string AfterRevealed(QuizQuestion q)
        {
            return "The answer was the " + _equipment.NameOf(q.correctObjectId) + ". " + q.explanation;
        }

        public string NoMoreHints() { return "No more hints are available for this task."; }

        public string SessionSummary(SessionReport r)
        {
            string s = "Session complete: " + r.tasksCompleted + " of " + r.tasksTotal + " identified, score " + r.score.ToString("F0")
                       + "/100 (" + (r.passed ? "pass" : "not yet passed") + ", threshold " + r.passThreshold.ToString("F0") + ").";
            if (r.reviewAreas.Count > 0) s += "\nSuggested review: " + string.Join(", ", r.reviewAreas) + ".";
            return s;
        }
    }
}
