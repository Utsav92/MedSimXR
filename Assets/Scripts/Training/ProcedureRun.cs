using System;
using System.Collections.Generic;

namespace MedSimXR.Training
{
    [Serializable]
    public class ProcedureStep
    {
        public string id;
        public string prompt;
        public string trigger;      // select | inspect | grab | placed | reset
        public string objectId;
        public string hint;
        public string explanation;
    }

    [Serializable]
    public class UnsafeEvent
    {
        public string eventType;
        public string objectId;     // empty = any object
        public string message;
    }

    [Serializable]
    public class ProcedureDef
    {
        public string moduleId;
        public string title;
        public string category;     // rubric category this procedure feeds: handling | sequence
        public string briefing;
        public float targetSeconds = 120f;
        public ProcedureStep[] steps;
        public UnsafeEvent[] unsafeEvents;
    }

    [Serializable]
    public class ProcedureFile
    {
        public string reviewStatus;
        public ProcedureDef[] procedures;

        public ProcedureDef Find(string moduleId)
        {
            if (procedures == null) return null;
            foreach (var p in procedures) if (p.moduleId == moduleId) return p;
            return null;
        }
    }

    [Serializable]
    public class StepResult
    {
        public string stepId;
        public bool done;
        public int errors;
        public int hintsUsed;
        public float seconds;
    }

    [Serializable]
    public class ProcedureResult
    {
        public string moduleId;
        public int stepsTotal;
        public int stepsDone;
        public int outOfOrder;
        public int wrongActions;
        public int unsafeActions;
        public int hintsUsed;
        public float seconds;
        public float targetSeconds;
        public List<StepResult> steps = new List<StepResult>();
    }

    public enum StepOutcome { Ignored, Correct, OutOfOrder, WrongAction, Unsafe, Completed }

    /// <summary>
    /// Pure state machine for an ordered procedure (safe handling, infection prevention).
    /// Events are fed in as (type, objectId) pairs, which come from the session log in the running app.
    /// Time is passed in so the logic is deterministic and unit-testable.
    /// </summary>
    public class ProcedureRun
    {
        readonly ProcedureDef _def;
        readonly HashSet<string> _triggerTypes = new HashSet<string>();
        int _index;
        float _start, _stepStart;

        public ProcedureResult Result { get; private set; }
        public bool Started { get; private set; }
        public bool Complete { get; private set; }
        public string LastMessage { get; private set; }
        public int Index { get { return _index; } }
        public int Total { get { return _def.steps.Length; } }
        public ProcedureDef Def { get { return _def; } }
        public ProcedureStep Current { get { return Started && !Complete ? _def.steps[_index] : null; } }
        public StepResult CurrentResult { get { return Started && !Complete ? Result.steps[_index] : null; } }

        public ProcedureRun(ProcedureDef def)
        {
            _def = def;
            foreach (var s in def.steps) _triggerTypes.Add(s.trigger);
        }

        public void Begin(float now)
        {
            Started = true;
            Complete = false;
            _index = 0;
            _start = _stepStart = now;
            Result = new ProcedureResult { moduleId = _def.moduleId, stepsTotal = _def.steps.Length, targetSeconds = _def.targetSeconds };
            foreach (var s in _def.steps) Result.steps.Add(new StepResult { stepId = s.id });
        }

        static bool Matches(ProcedureStep s, string type, string objectId)
        {
            return s.trigger == type && s.objectId == objectId;
        }

        public StepOutcome Observe(string type, string objectId, float now)
        {
            if (!Started || Complete) return StepOutcome.Ignored;
            LastMessage = null;

            if (_def.unsafeEvents != null)
            {
                foreach (var u in _def.unsafeEvents)
                {
                    if (u.eventType != type) continue;
                    if (!string.IsNullOrEmpty(u.objectId) && u.objectId != objectId) continue;
                    Result.unsafeActions++;
                    Result.steps[_index].errors++;
                    LastMessage = u.message;
                    return StepOutcome.Unsafe;
                }
            }

            if (!_triggerTypes.Contains(type)) return StepOutcome.Ignored;

            var cur = _def.steps[_index];
            if (Matches(cur, type, objectId))
            {
                var sr = Result.steps[_index];
                sr.done = true;
                sr.seconds = now - _stepStart;
                Result.stepsDone++;
                _index++;
                _stepStart = now;
                if (_index >= _def.steps.Length)
                {
                    Complete = true;
                    Result.seconds = now - _start;
                    return StepOutcome.Completed;
                }
                return StepOutcome.Correct;
            }

            // A repeat of something already done is harmless.
            for (int i = 0; i < _index; i++) if (Matches(_def.steps[i], type, objectId)) return StepOutcome.Ignored;

            // Something that belongs later in the procedure was done too early.
            for (int i = _index + 1; i < _def.steps.Length; i++)
            {
                if (!Matches(_def.steps[i], type, objectId)) continue;
                Result.outOfOrder++;
                Result.steps[_index].errors++;
                LastMessage = "Out of sequence. Right now: " + cur.prompt;
                return StepOutcome.OutOfOrder;
            }

            if (type == "select")
            {
                Result.wrongActions++;
                Result.steps[_index].errors++;
                LastMessage = "That is not the item for this step. Right now: " + cur.prompt;
                return StepOutcome.WrongAction;
            }
            return StepOutcome.Ignored;
        }

        public string RequestHint()
        {
            if (!Started || Complete) return null;
            Result.hintsUsed++;
            Result.steps[_index].hintsUsed++;
            return _def.steps[_index].hint;
        }

        /// <summary>Records elapsed time for an unfinished run (used when a session is ended early).</summary>
        public void Stop(float now)
        {
            if (!Complete) Result.seconds = now - _start;
        }
    }
}
