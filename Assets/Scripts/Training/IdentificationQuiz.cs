using System;
using System.Collections.Generic;

namespace MedSimXR.Training
{
    [Serializable]
    public class QuizQuestion
    {
        public string id;
        public string objectId;
        public string correctObjectId;
        public string prompt;
        public string[] hints;
        public string explanation;
        public string assessmentCriteria;
        public int maxAttempts = 3;
        public float targetSeconds = 30f;
    }

    [Serializable]
    public class QuizSet
    {
        public string moduleId;
        public string title;
        public string reviewStatus;
        public QuizQuestion[] questions;
    }

    [Serializable]
    public class QuestionResult
    {
        public string questionId;
        public string correctObjectId;
        public bool correct;
        public bool revealed;      // answer was shown after too many wrong attempts, or skipped
        public int wrongAttempts;
        public int hintsUsed;
        public float seconds;
        public float targetSeconds;
        public List<string> wrongSelections = new List<string>();
    }

    public enum SubmitOutcome { Ignored, Correct, Wrong, Revealed }

    /// <summary>
    /// Pure state machine for the identification exercise. Time is passed in so it is deterministic and testable.
    /// </summary>
    public class IdentificationQuiz
    {
        readonly QuizQuestion[] _questions;
        int _index = -1;
        float _startTime;
        QuestionResult _current;

        public List<QuestionResult> Results { get; private set; }
        public bool Started { get; private set; }
        public bool Complete { get; private set; }
        public bool AwaitingContinue { get; private set; }
        public int Index { get { return _index; } }
        public int Total { get { return _questions.Length; } }
        public QuizQuestion Current { get { return (_index >= 0 && _index < _questions.Length) ? _questions[_index] : null; } }
        public QuestionResult CurrentResult { get { return _current; } }

        public IdentificationQuiz(QuizSet set)
        {
            if (set == null || set.questions == null || set.questions.Length == 0)
                throw new ArgumentException("Quiz set has no questions");
            _questions = set.questions;
            Results = new List<QuestionResult>();
        }

        public void Begin(float now)
        {
            Results.Clear();
            Complete = false;
            Started = true;
            _index = 0;
            BeginQuestion(now);
        }

        void BeginQuestion(float now)
        {
            _startTime = now;
            AwaitingContinue = false;
            var q = Current;
            _current = new QuestionResult { questionId = q.id, correctObjectId = q.correctObjectId, targetSeconds = q.targetSeconds };
        }

        void FinishQuestion(float now)
        {
            _current.seconds = now - _startTime;
            Results.Add(_current);
            AwaitingContinue = true;
        }

        public SubmitOutcome Submit(string objectId, float now)
        {
            if (!Started || Complete || AwaitingContinue || string.IsNullOrEmpty(objectId)) return SubmitOutcome.Ignored;
            if (objectId == Current.correctObjectId)
            {
                _current.correct = true;
                FinishQuestion(now);
                return SubmitOutcome.Correct;
            }
            _current.wrongAttempts++;
            _current.wrongSelections.Add(objectId);
            if (_current.wrongAttempts >= Current.maxAttempts)
            {
                _current.revealed = true;
                FinishQuestion(now);
                return SubmitOutcome.Revealed;
            }
            return SubmitOutcome.Wrong;
        }

        /// <summary>Returns the next hint, or null when none are left.</summary>
        public string RequestHint()
        {
            if (!Started || Complete || AwaitingContinue) return null;
            var hints = Current.hints;
            if (hints == null || _current.hintsUsed >= hints.Length) return null;
            return hints[_current.hintsUsed++];
        }

        public bool Skip(float now)
        {
            if (!Started || Complete || AwaitingContinue) return false;
            _current.revealed = true;
            FinishQuestion(now);
            return true;
        }

        public void Continue(float now)
        {
            if (!AwaitingContinue) return;
            _index++;
            if (_index >= _questions.Length) { Complete = true; AwaitingContinue = false; return; }
            BeginQuestion(now);
        }
    }
}
