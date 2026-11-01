using System;
using System.Collections.Generic;

namespace MedSimXR.Training
{
    [Serializable]
    public class KnowledgeQuestion
    {
        public string id;
        public string objectId;
        public string prompt;
        public string[] options;
        public int correctIndex;
        public string hint;
        public string explanation;
        public float targetSeconds = 30f;
    }

    [Serializable]
    public class KnowledgeSet
    {
        public string moduleId;
        public string title;
        public string reviewStatus;
        public KnowledgeQuestion[] questions;
    }

    [Serializable]
    public class KnowledgeResult
    {
        public string questionId;
        public int chosenIndex = -1;
        public bool correct;
        public int hintsUsed;
        public float seconds;
        public float targetSeconds;
    }

    public enum KnowledgeOutcome { Ignored, Correct, Wrong }

    /// <summary>Multiple-choice state machine. One answer per question, then the explanation is shown.</summary>
    public class KnowledgeQuiz
    {
        readonly KnowledgeQuestion[] _questions;
        int _index = -1;
        float _questionStart;
        KnowledgeResult _current;

        public List<KnowledgeResult> Results { get; private set; }
        public bool Started { get; private set; }
        public bool Complete { get; private set; }
        public bool AwaitingContinue { get; private set; }
        public int Index { get { return _index; } }
        public int Total { get { return _questions.Length; } }
        public KnowledgeQuestion Current { get { return Started && !Complete && _index >= 0 ? _questions[_index] : null; } }
        public KnowledgeResult CurrentResult { get { return _current; } }
        public string Title { get; private set; }

        public KnowledgeQuiz(KnowledgeSet set)
        {
            _questions = set.questions;
            Title = set.title;
        }

        public void Begin(float now)
        {
            Results = new List<KnowledgeResult>();
            Started = true;
            Complete = false;
            AwaitingContinue = false;
            _index = -1;
            Advance(now);
        }

        void Advance(float now)
        {
            _index++;
            if (_index >= _questions.Length) { Complete = true; _current = null; return; }
            var q = _questions[_index];
            _current = new KnowledgeResult { questionId = q.id, targetSeconds = q.targetSeconds };
            _questionStart = now;
            AwaitingContinue = false;
        }

        public KnowledgeOutcome Answer(int optionIndex, float now)
        {
            if (!Started || Complete || AwaitingContinue || _current == null) return KnowledgeOutcome.Ignored;
            var q = _questions[_index];
            if (optionIndex < 0 || optionIndex >= q.options.Length) return KnowledgeOutcome.Ignored;
            _current.chosenIndex = optionIndex;
            _current.correct = optionIndex == q.correctIndex;
            _current.seconds = now - _questionStart;
            Results.Add(_current);
            AwaitingContinue = true;
            return _current.correct ? KnowledgeOutcome.Correct : KnowledgeOutcome.Wrong;
        }

        public string RequestHint()
        {
            if (!Started || Complete || AwaitingContinue || _current == null) return null;
            _current.hintsUsed++;
            return _questions[_index].hint;
        }

        public void Continue(float now)
        {
            if (!AwaitingContinue) return;
            Advance(now);
        }
    }
}
