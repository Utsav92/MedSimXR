using System;
using System.IO;
using System.Text;
using MedSimXR.Assessment;
using MedSimXR.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedSimXR.Training
{
    /// <summary>Phase 2 modules: B safe handling, C patient monitoring, D infection prevention, E CT orientation, dashboard.</summary>
    public partial class TrainingManager
    {
        /// <summary>Test hook: added to Time.time for the Phase 2 modules so automated tests can jump forward in time.</summary>
        public float timeOffset;

        string _overlayTitleBeforePause, _overlayBodyBeforePause;
        ProcedureFile _procFile;
        MonitoringFile _monFile;
        KnowledgeSet _knowledgeSet;
        ProgressStore _progress;
        string _progressDir;
        DateTime _moduleStartedUtc;
        float _moduleStart;
        bool _awaitingParam;
        string _knowledgeHint;
        int _lastMonitoringScenario = -1;
        int _lastRenderedQuestion = -1;

        public ProcedureRun Procedure { get; private set; }
        public MonitoringRun Monitoring { get; private set; }
        public KnowledgeQuiz Knowledge { get; private set; }
        public ModuleReport LastModuleReport { get; private set; }
        public string LastModuleReportPath { get; private set; }
        public ScoringEngine Scoring { get { return _scoring; } }

        float Now { get { return Time.time + timeOffset; } }

        /// <summary>Progress is created lazily so it follows reportDirectory even if that is changed after Awake (tests do this).</summary>
        public ProgressStore Progress
        {
            get
            {
                string dir = saveReports ? reportDirectory : null;
                if (_progress == null || _progressDir != dir) { _progressDir = dir; _progress = new ProgressStore(dir); }
                return _progress;
            }
        }

        void InitModules()
        {
            try { _procFile = DataLoader.Load<ProcedureFile>("procedures"); }
            catch (Exception e) { Debug.LogError("procedures.json: " + e.Message); }
            try { _monFile = DataLoader.Load<MonitoringFile>("monitoring_scenarios"); }
            catch (Exception e) { Debug.LogError("monitoring_scenarios.json: " + e.Message); }
            try { _knowledgeSet = DataLoader.Load<KnowledgeSet>("ct_orientation_questions"); }
            catch (Exception e) { Debug.LogError("ct_orientation_questions.json: " + e.Message); }
        }

        // ---------- shared helpers ----------
        void BeginModule(AppMode mode)
        {
            SessionLog.Clear();
            _moduleStartedUtc = DateTime.UtcNow;
            _moduleStart = Now;
            _awaitingParam = false;
            _knowledgeHint = null;
            _lastRenderedQuestion = -1;
            hud.HideOverlay();
            hud.ShowInfo(null);
            hud.SetMonitor(null);
            hud.ShowFeedback("", Neutral);
            SetMode(mode);
        }

        void ClearModuleState()
        {
            Procedure = null;
            Monitoring = null;
            Knowledge = null;
            _awaitingParam = false;
            _lastMonitoringScenario = -1;
        }

        public void ReturnToRoom()
        {
            hud.HideOverlay();
            hud.SetMonitor(null);
            hud.SetTask(null);
            ClearModuleState();
            SetMode(AppMode.Explore);
            ShowExploreHint();
        }

        /// <summary>Leave a running module without recording a result.</summary>
        public void CancelModule()
        {
            if (Mode != AppMode.Quiz && Mode != AppMode.Procedure && Mode != AppMode.Monitoring && Mode != AppMode.Knowledge) return;
            SessionLog.Log("module_abandoned", "", Mode.ToString());
            ReturnToRoom();
            hud.ShowFeedback("Module left. Nothing was recorded.", Neutral);
        }

        void RecordModule(ModuleReport report, bool writeFiles)
        {
            LastModuleReport = report;
            LastModuleReportPath = null;
            Progress.Record(report);
            if (saveReports)
            {
                try
                {
                    if (writeFiles) LastModuleReportPath = report.Save(reportDirectory);
                    Progress.Save();
                }
                catch (Exception e) { Debug.LogWarning("Could not save module report: " + e.Message); }
            }
            SessionLog.Log("module_complete", report.moduleId, "score=" + report.score.ToString("F1"));
        }

        void ShowModuleResults(ModuleReport rep, string title)
        {
            RecordModule(rep, true);
            SetMode(AppMode.Results);
            hud.SetTask(null);
            hud.SetMonitor(null);
            hud.ShowFeedback("", Neutral);
            var sb = new StringBuilder();
            sb.Append("Score ").Append(rep.score.ToString("F0")).Append(" / 100  -  ").Append(rep.passed ? "pass" : "not yet passed")
              .Append(" (threshold ").Append(rep.passThreshold.ToString("F0")).Append(")\n");
            var cats = new System.Collections.Generic.List<string>();
            foreach (var c in rep.categories) cats.Add(c.category + " " + c.value.ToString("F0"));
            sb.Append("Categories: ").Append(string.Join("   ", cats)).Append("    Time: ").Append(rep.durationSeconds.ToString("F0")).Append(" s\n\n");
            foreach (var f in rep.feedback) sb.Append(f).Append("\n");
            if (rep.reviewAreas.Count > 0) sb.Append("\nSuggested review: ").Append(string.Join(", ", rep.reviewAreas)).Append("\n");
            if (LastModuleReportPath != null) sb.Append("\nReport saved: ").Append(LastModuleReportPath).Append("\n");
            sb.Append("\nPrototype scoring: weights are provisional, not clinically validated.\n\n[Enter] return to the room");
            hud.ShowOverlay("RESULTS - " + title, sb.ToString());
        }

        // ---------- per-frame ----------
        void TickModules()
        {
            if (Mode == AppMode.Monitoring && Monitoring != null) TickMonitoring();
        }

        void UpdateModuleKeys(Keyboard kb)
        {
            if ((Mode == AppMode.Quiz || Mode == AppMode.Procedure || Mode == AppMode.Monitoring || Mode == AppMode.Knowledge) && kb.qKey.wasPressedThisFrame)
            {
                CancelModule();
                return;
            }
            switch (Mode)
            {
                case AppMode.Procedure:
                    if (kb.hKey.wasPressedThisFrame) RequestProcedureHint();
                    break;
                case AppMode.Monitoring:
                    if (kb.nKey.wasPressedThisFrame) OpenReportPrompt();
                    if (_awaitingParam)
                    {
                        if (kb.digit1Key.wasPressedThisFrame) ReportParam("hr");
                        else if (kb.digit2Key.wasPressedThisFrame) ReportParam("spo2");
                        else if (kb.digit3Key.wasPressedThisFrame) ReportParam("rr");
                        else if (kb.digit4Key.wasPressedThisFrame) ReportParam("bp");
                        else if (kb.digit5Key.wasPressedThisFrame) ReportParam("temp");
                        else if (kb.digit0Key.wasPressedThisFrame) { _awaitingParam = false; hud.ShowFeedback("Cancelled.", Neutral); }
                    }
                    break;
                case AppMode.Knowledge:
                    if (kb.digit1Key.wasPressedThisFrame) AnswerKnowledge(0);
                    else if (kb.digit2Key.wasPressedThisFrame) AnswerKnowledge(1);
                    else if (kb.digit3Key.wasPressedThisFrame) AnswerKnowledge(2);
                    else if (kb.digit4Key.wasPressedThisFrame) AnswerKnowledge(3);
                    if (kb.hKey.wasPressedThisFrame) RequestKnowledgeHint();
                    if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) ContinueKnowledge();
                    break;
            }
        }

        static string Clock(float t) { return ((int)t / 60).ToString("00") + ":" + ((int)t % 60).ToString("00"); }

        bool UpdateModuleStatus()
        {
            float t = Now - _moduleStart;
            if (Mode == AppMode.Procedure && Procedure != null)
            {
                int errs = Procedure.Result.outOfOrder + Procedure.Result.wrongActions + Procedure.Result.unsafeActions;
                hud.SetStatus("Step " + Math.Min(Procedure.Index + 1, Procedure.Total) + "/" + Procedure.Total + "   Errors " + errs
                              + "   Hints " + Procedure.Result.hintsUsed + "   Time " + Clock(t) + "   Q: leave");
                return true;
            }
            if (Mode == AppMode.Monitoring && Monitoring != null)
            {
                hud.SetStatus("Scenario " + (Monitoring.ScenarioIndex + 1) + "/" + Monitoring.ScenarioCount + "   Time " + Clock(t) + "   N: report   Q: leave");
                return true;
            }
            if (Mode == AppMode.Knowledge && Knowledge != null)
            {
                hud.SetStatus("Question " + Math.Min(Knowledge.Index + 1, Knowledge.Total) + "/" + Knowledge.Total + "   Time " + Clock(t));
                return true;
            }
            return false;
        }

        // ---------- modules B and D: ordered procedures ----------
        public bool StartProcedure(string moduleId)
        {
            var def = _procFile != null ? _procFile.Find(moduleId) : null;
            if (def == null)
            {
                hud.ShowFeedback("This module's data could not be loaded.", Bad);
                return false;
            }
            BeginModule(AppMode.Procedure);
            Procedure = new ProcedureRun(def);
            Procedure.Begin(Now);
            SessionLog.Log("module_start", "", moduleId);
            ShowProcedureTask();
            hud.ShowFeedback(def.briefing + "\nH = hint, Q = leave the module.", Neutral);
            return true;
        }

        void ShowProcedureTask()
        {
            if (Procedure == null || Procedure.Current == null) return;
            hud.SetTask("Step " + (Procedure.Index + 1) + " of " + Procedure.Total + ": " + Procedure.Current.prompt);
        }

        void HandleLogEvent(LogEvent e)
        {
            if (Mode != AppMode.Procedure || Procedure == null || Procedure.Complete) return;
            switch (e.type)
            {
                case "select":
                case "inspect":
                case "grab":
                case "placed":
                case "reset":
                case "dropped":
                case "grab_denied":
                    ProcessProcedureEvent(e.type, e.objectId);
                    break;
            }
        }

        /// <summary>Feeds one interaction into the running procedure and updates the HUD. Public so tests can drive it.</summary>
        public StepOutcome ProcessProcedureEvent(string type, string objectId)
        {
            if (Mode != AppMode.Procedure || Procedure == null) return StepOutcome.Ignored;
            var before = Procedure.Current;
            var outcome = Procedure.Observe(type, objectId, Now);
            switch (outcome)
            {
                case StepOutcome.Correct:
                    hud.ShowFeedback("Done. " + before.explanation, Good);
                    ShowProcedureTask();
                    break;
                case StepOutcome.Completed:
                    FinishProcedure();
                    break;
                case StepOutcome.OutOfOrder:
                case StepOutcome.WrongAction:
                case StepOutcome.Unsafe:
                    hud.ShowFeedback(Procedure.LastMessage, Bad);
                    break;
            }
            if (outcome != StepOutcome.Ignored) SessionLog.Log("procedure_step", objectId, outcome.ToString());
            return outcome;
        }

        public string RequestProcedureHint()
        {
            if (Mode != AppMode.Procedure || Procedure == null) return null;
            string h = Procedure.RequestHint();
            hud.ShowFeedback(h != null ? "Hint: " + h : _instructor.NoMoreHints(), Neutral);
            SessionLog.Log("hint", "", h ?? "none");
            return h;
        }

        void FinishProcedure()
        {
            var def = Procedure.Def;
            var res = Procedure.Result;
            var b = _scoring.ScoreProcedure(def, res);
            var rep = ModuleReport.FromProcedure(def, res, b, _scoring.PassThreshold, _moduleStartedUtc, Equipment.NameOf);
            ShowModuleResults(rep, def.title);
        }

        // ---------- module C: patient monitoring (synthetic data) ----------
        public bool StartMonitoring()
        {
            if (_monFile == null || _monFile.scenarios == null || _monFile.scenarios.Length == 0)
            {
                hud.ShowFeedback("This module's data could not be loaded.", Bad);
                return false;
            }
            BeginModule(AppMode.Monitoring);
            Monitoring = new MonitoringRun(_monFile);
            Monitoring.Begin(Now);
            _lastMonitoringScenario = -1;
            SessionLog.Log("module_start", "", ProgressStore.ModuleC);
            TickMonitoring();
            return true;
        }

        void TickMonitoring()
        {
            Monitoring.Tick(Now);
            if (Monitoring.Complete) { FinishMonitoring(); return; }
            if (Monitoring.ScenarioIndex != _lastMonitoringScenario)
            {
                _lastMonitoringScenario = Monitoring.ScenarioIndex;
                _awaitingParam = false;
                var s = Monitoring.CurrentScenario;
                hud.SetTask("Monitoring " + (Monitoring.ScenarioIndex + 1) + " of " + Monitoring.ScenarioCount + " - SIMULATED DATA\n" + s.title);
                hud.ShowFeedback(s.briefing + "\nReport: click the monitor or press N, then choose the value.", Neutral);
            }
            hud.SetMonitor(FormatMonitor());
        }

        string FormatMonitor()
        {
            var v = Monitoring.CurrentDisplay();
            var sb = new StringBuilder();
            sb.Append("SIMULATED MONITOR\n");
            sb.Append("HR     ").Append(Mathf.Round(v.hr)).Append(" bpm   ").Append(RangeText("hr")).Append("\n");
            sb.Append("SpO2   ").Append(Mathf.Round(v.spo2)).Append(" %   ").Append(RangeText("spo2")).Append("\n");
            sb.Append("RR     ").Append(Mathf.Round(v.rr)).Append(" /min   ").Append(RangeText("rr")).Append("\n");
            sb.Append("BP     ").Append(Mathf.Round(v.sys)).Append("/").Append(Mathf.Round(v.dia)).Append(" mmHg   ")
              .Append(RangeText("sys")).Append(" / ").Append(RangeText("dia")).Append("\n");
            sb.Append("Temp   ").Append(v.temp.ToString("F1")).Append(" C   ").Append(RangeText("temp"));
            return sb.ToString();
        }

        string RangeText(string vital)
        {
            var r = Monitoring.RangeOf(vital);
            return r == null ? "" : "[" + r.low + "-" + r.high + "]";
        }

        public void OpenReportPrompt()
        {
            if (Mode != AppMode.Monitoring || Monitoring == null || Monitoring.Complete) return;
            _awaitingParam = true;
            VrCursor = 0;
            hud.ShowFeedback("Which value is out of range?   1 Heart rate   2 SpO2   3 Resp. rate   4 Blood pressure   5 Temperature   (0 = cancel)", Neutral);
        }

        static string ParamLabel(string p)
        {
            switch (p)
            {
                case "hr": return "Heart rate";
                case "spo2": return "SpO2";
                case "rr": return "Respiratory rate";
                case "bp": return "Blood pressure";
                case "temp": return "Temperature";
                default: return p;
            }
        }

        /// <summary>The learner reports a parameter as out of range. Public so tests can drive it.</summary>
        public ReportOutcome ReportParam(string param)
        {
            if (Mode != AppMode.Monitoring || Monitoring == null) return ReportOutcome.Ignored;
            _awaitingParam = false;
            var outcome = Monitoring.Report(param, Now);
            if (Monitoring.Complete) { FinishMonitoring(); return outcome; }
            SessionLog.Log("monitor_report", param, outcome.ToString());
            switch (outcome)
            {
                case ReportOutcome.Correct:
                    hud.ShowFeedback("Correct - " + ParamLabel(param) + " is out of range. Well spotted.", Good);
                    break;
                case ReportOutcome.Duplicate:
                    hud.ShowFeedback(ParamLabel(param) + " was already reported.", Neutral);
                    break;
                case ReportOutcome.WrongParameter:
                    hud.ShowFeedback(ParamLabel(param) + " is within its range right now. Another value is out of range - keep looking.", Bad);
                    break;
                case ReportOutcome.FalseAlarm:
                    hud.ShowFeedback("False alarm: every value is within its range right now.", Bad);
                    break;
            }
            return outcome;
        }

        void FinishMonitoring()
        {
            var res = Monitoring.Result;
            var b = _scoring.ScoreMonitoring(res);
            var rep = ModuleReport.FromMonitoring(_monFile, res, b, _scoring.PassThreshold, _moduleStartedUtc, Equipment.NameOf);
            ShowModuleResults(rep, _monFile.title);
        }

        // ---------- module E: orientation quiz ----------
        public bool StartKnowledge()
        {
            if (_knowledgeSet == null || _knowledgeSet.questions == null || _knowledgeSet.questions.Length == 0)
            {
                hud.ShowFeedback("This module's data could not be loaded.", Bad);
                return false;
            }
            BeginModule(AppMode.Knowledge);
            Knowledge = new KnowledgeQuiz(_knowledgeSet);
            Knowledge.Begin(Now);
            SessionLog.Log("module_start", "", _knowledgeSet.moduleId);
            RenderKnowledge();
            return true;
        }

        void RenderKnowledge()
        {
            var q = Knowledge != null ? Knowledge.Current : null;
            if (q == null) return;
            if (Knowledge.Index != _lastRenderedQuestion) { _lastRenderedQuestion = Knowledge.Index; VrCursor = 0; }
            var sb = new StringBuilder();
            sb.Append("Question ").Append(Knowledge.Index + 1).Append(" of ").Append(Knowledge.Total).Append("\n\n").Append(q.prompt).Append("\n\n");
            for (int i = 0; i < q.options.Length; i++) sb.Append(i + 1).Append("   ").Append(q.options[i]).Append("\n");
            if (Knowledge.AwaitingContinue)
            {
                var r = Knowledge.CurrentResult;
                sb.Append("\n").Append(r.correct ? "Correct. " : "Not quite - the answer was option " + (q.correctIndex + 1) + ". ").Append(q.explanation).Append("\n\n[Enter] continue");
            }
            else
            {
                if (!string.IsNullOrEmpty(_knowledgeHint)) sb.Append("\nHint: ").Append(_knowledgeHint).Append("\n");
                sb.Append("\nPress 1-").Append(q.options.Length).Append(" to answer    H hint    Q leave");
            }
            hud.ShowOverlay("MODULE E - ORIENTATION QUIZ", sb.ToString());
        }

        public KnowledgeOutcome AnswerKnowledge(int optionIndex)
        {
            if (Mode != AppMode.Knowledge || Knowledge == null) return KnowledgeOutcome.Ignored;
            var q = Knowledge.Current;
            var o = Knowledge.Answer(optionIndex, Now);
            if (o != KnowledgeOutcome.Ignored)
            {
                SessionLog.Log("knowledge_answer", q != null ? q.id : "", o.ToString());
                RenderKnowledge();
            }
            return o;
        }

        public string RequestKnowledgeHint()
        {
            if (Mode != AppMode.Knowledge || Knowledge == null) return null;
            string h = Knowledge.RequestHint();
            if (h != null) { _knowledgeHint = h; SessionLog.Log("hint", "", h); RenderKnowledge(); }
            return h;
        }

        public void ContinueKnowledge()
        {
            if (Mode != AppMode.Knowledge || Knowledge == null || !Knowledge.AwaitingContinue) return;
            Knowledge.Continue(Now);
            _knowledgeHint = null;
            if (Knowledge.Complete) FinishKnowledge();
            else RenderKnowledge();
        }

        void FinishKnowledge()
        {
            var b = _scoring.ScoreKnowledge(Knowledge.Results, id =>
            {
                foreach (var q in _knowledgeSet.questions) if (q.id == id) return q.prompt;
                return id;
            });
            var rep = ModuleReport.FromKnowledge(_knowledgeSet, Knowledge.Results, b, _scoring.PassThreshold, _moduleStartedUtc, Now - _moduleStart);
            ShowModuleResults(rep, _knowledgeSet.title);
        }

        // ---------- dashboard ----------
        public void ShowDashboard()
        {
            SetMode(AppMode.Results);
            hud.SetTask(null);
            hud.SetMonitor(null);
            hud.ShowFeedback("", Neutral);
            hud.ShowOverlay("PROGRESS DASHBOARD", Progress.DashboardText(_scoring) + "\n[Enter] return to the room");
            SessionLog.Log("dashboard_open");
        }
    }
}
