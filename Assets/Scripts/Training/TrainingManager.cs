using System;
using System.IO;
using MedSimXR.AIInstructor;
using MedSimXR.Assessment;
using MedSimXR.Core;
using MedSimXR.Core.UI;
using MedSimXR.Equipment;
using MedSimXR.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedSimXR.Training
{
    public enum AppMode { Explore, Menu, Quiz, Results, Paused, Procedure, Monitoring, Knowledge }

    /// <summary>
    /// Owns the application mode and runs training module A (equipment identification).
    /// Keys: Tab menu, Esc pause, H hint, Enter continue, X skip task.
    /// </summary>
    public partial class TrainingManager : MonoBehaviour
    {
        public HudView hud;
        public InteractionSystem interaction;
        public FirstPersonController player;
        public bool lockCursor = true;
        public bool saveReports = true;
        public string reportDirectory;

        public AppMode Mode { get; private set; } = AppMode.Explore;
        public EquipmentRegistry Equipment { get; private set; }
        public IdentificationQuiz Quiz { get; private set; }
        public SessionReport LastReport { get; private set; }
        public string LastReportPath { get; private set; }

        QuizSet _quizSet, _coreSet, _advancedSet;
        ScoringEngine _scoring;
        LocalInstructor _instructor;
        AppMode _modeBeforePause;
        DateTime _startedUtc;
        float _startTime;
        static readonly Color Good = new Color(0.55f, 1f, 0.65f), Bad = new Color(1f, 0.6f, 0.5f), Neutral = Color.white;

        public LocalInstructor Instructor { get { return _instructor; } }

        void Awake()
        {
            Equipment = EquipmentRegistry.LoadDefault();
            _quizSet = _coreSet = DataLoader.Load<QuizSet>("identification_questions");
            try { _advancedSet = DataLoader.Load<QuizSet>("identification_advanced"); }
            catch (Exception e) { Debug.LogError("identification_advanced.json: " + e.Message); }
            _scoring = new ScoringEngine(Rubric.LoadDefault());
            _instructor = new LocalInstructor(Equipment);
            if (string.IsNullOrEmpty(reportDirectory)) reportDirectory = Path.Combine(Application.persistentDataPath, "Reports");
            InitModules();
        }

        void Start()
        {
            if (interaction != null)
            {
                interaction.OnSelected += HandleSelected;
                interaction.OnInspected += HandleInspected;
                interaction.OnHoverChanged += HandleHover;
            }
            SessionLog.OnEvent += HandleLogEvent;
            SetMode(AppMode.Explore);
            ShowExploreHint();
        }

        void OnDestroy()
        {
            if (interaction != null)
            {
                interaction.OnSelected -= HandleSelected;
                interaction.OnInspected -= HandleInspected;
                interaction.OnHoverChanged -= HandleHover;
            }
            SessionLog.OnEvent -= HandleLogEvent;
            Time.timeScale = 1f;
        }

        // ---------- input ----------
        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame) TogglePause();
                else if (Mode != AppMode.Paused)
                {
                    if (kb.tabKey.wasPressedThisFrame) { if (Mode == AppMode.Menu) CloseMenu(); else if (Mode == AppMode.Explore) OpenMenu(); }
                    if (Mode == AppMode.Menu)
                    {
                        if (kb.digit1Key.wasPressedThisFrame) ChooseMenu(1);
                        else if (kb.digit2Key.wasPressedThisFrame) ChooseMenu(2);
                        else if (kb.digit3Key.wasPressedThisFrame) ChooseMenu(3);
                        else if (kb.digit4Key.wasPressedThisFrame) ChooseMenu(4);
                        else if (kb.digit5Key.wasPressedThisFrame) ChooseMenu(5);
                        else if (kb.digit6Key.wasPressedThisFrame) ChooseMenu(6);
                        else if (kb.digit7Key.wasPressedThisFrame) ChooseMenu(7);
                        else if (kb.digit8Key.wasPressedThisFrame) ChooseMenu(8);
                    }
                    UpdateModuleKeys(kb);
                    if (Mode == AppMode.Quiz)
                    {
                        if (kb.hKey.wasPressedThisFrame) RequestHint();
                        if (kb.xKey.wasPressedThisFrame) SkipQuestion();
                        if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) ContinueQuiz();
                    }
                    if (Mode == AppMode.Results && kb.enterKey.wasPressedThisFrame) ReturnToRoom();
                }
            }
            TickModules();
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (hud == null) return;
            if (Mode == AppMode.Quiz && Quiz != null && Quiz.Started)
            {
                int errs = 0, hints = 0;
                foreach (var r in Quiz.Results) { errs += r.wrongAttempts; hints += r.hintsUsed; }
                if (Quiz.CurrentResult != null && !Quiz.AwaitingContinue) { errs += Quiz.CurrentResult.wrongAttempts; hints += Quiz.CurrentResult.hintsUsed; }
                float t = Time.time - _startTime;
                hud.SetStatus("Task " + Math.Min(Quiz.Index + 1, Quiz.Total) + "/" + Quiz.Total + "   Errors " + errs + "   Hints " + hints
                              + "   Time " + ((int)t / 60).ToString("00") + ":" + ((int)t % 60).ToString("00"));
            }
            else if (!UpdateModuleStatus()) hud.SetStatus("Tab: training menu    Esc: pause");
            int n = VrActive ? VrOptionCount : 0;
            if (n > 0) hud.SetStatus(hud.StatusText + "\nVR: option " + (VrCursor + 1) + " of " + n + " - left stick up/down to choose, A to confirm");
        }

        // ---------- mode handling ----------
        public void SetMode(AppMode m)
        {
            Mode = m;
            VrCursor = 0;
            bool play = m == AppMode.Explore || m == AppMode.Quiz || m == AppMode.Procedure || m == AppMode.Monitoring;
            if (player != null) player.inputEnabled = play;
            if (interaction != null) interaction.inputEnabled = play;
            if (hud != null) hud.SetCrosshairVisible(play);
            // Names on panels would give the answers away, so hide them during the quiz.
            foreach (var p in FindObjectsByType<WorldInfoPanel>(FindObjectsInactive.Include)) p.SetVisible(m != AppMode.Quiz);
            if (hud != null && interaction != null) { hud.ShowInfo(null); HandleHover(interaction.Hovered); }
            if (lockCursor && Application.isPlaying)
            {
                Cursor.lockState = play ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !play;
            }
        }

        public void OpenMenu()
        {
            SetMode(AppMode.Menu);
            hud.ShowOverlay("TRAINING MENU",
                "1   Module A - Equipment Identification (core, 10 tasks)\n2   Module B - Safe Handling of Supplies\n3   Module C - Patient Monitoring (simulated)\n"
                + "4   Module D - Infection Prevention Sequence\n5   Module E - CT Injection System Orientation (quiz)\n6   Module F - Advanced Identification (all equipment)\n7   Progress dashboard\n8   Back to free exploration\n\n"
                + "Controls: WASD move, mouse look, E inspect, Left click select, G grab/release, R reset, Q leave a module, Tab menu, Esc pause\n\n"
                + "Prototype: content has not been clinically reviewed. Scoring weights are provisional.");
        }

        public void CloseMenu()
        {
            hud.HideOverlay();
            SetMode(AppMode.Explore);
            ShowExploreHint();
        }

        public void TogglePause()
        {
            if (Mode == AppMode.Paused)
            {
                Time.timeScale = 1f;
                hud.HideOverlay();
                SetMode(_modeBeforePause);
                if (_modeBeforePause == AppMode.Menu) OpenMenu();
                else if (_overlayTitleBeforePause != null) hud.ShowOverlay(_overlayTitleBeforePause, _overlayBodyBeforePause);
                _overlayTitleBeforePause = null;
                return;
            }
            _modeBeforePause = Mode;
            _overlayTitleBeforePause = hud.OverlayVisible ? hud.OverlayTitle : null;
            _overlayBodyBeforePause = hud.OverlayVisible ? hud.OverlayBody : null;
            SetMode(AppMode.Paused);
            Time.timeScale = 0f;
            hud.ShowOverlay("PAUSED", "Press Esc to resume.");
        }

        void ShowExploreHint()
        {
            hud.SetTask(null);
            hud.ShowFeedback("Explore the room. Look at an object: E to inspect, G to grab small items. Press Tab for the training menu.", Neutral);
        }

        // ---------- module A: identification ----------
        public void StartIdentification()
        {
            _quizSet = _coreSet;
            BeginIdentification();
        }

        /// <summary>Module F: identification across every piece of equipment and anatomy model in the room.</summary>
        public void StartAdvancedIdentification()
        {
            if (_advancedSet == null || _advancedSet.questions == null || _advancedSet.questions.Length == 0)
            {
                hud.ShowFeedback("This module's data could not be loaded.", Bad);
                return;
            }
            _quizSet = _advancedSet;
            BeginIdentification();
        }

        void BeginIdentification()
        {
            SessionLog.Clear();
            Quiz = new IdentificationQuiz(_quizSet);
            _startedUtc = DateTime.UtcNow;
            _startTime = Time.time;
            hud.HideOverlay();
            hud.ShowInfo(null);
            SetMode(AppMode.Quiz);
            Quiz.Begin(Time.time);
            SessionLog.Log("quiz_start", "", _quizSet.moduleId);
            ShowTask();
            hud.ShowFeedback("Look at the object and left-click it to answer. H = hint, X = skip.", Neutral);
        }

        void ShowTask()
        {
            hud.SetTask(_instructor.TaskInstruction(Quiz.Current, Quiz.Index, Quiz.Total));
        }

        public SubmitOutcome SubmitSelection(string objectId)
        {
            if (Mode != AppMode.Quiz || Quiz == null) return SubmitOutcome.Ignored;
            var q = Quiz.Current;
            var outcome = Quiz.Submit(objectId, Time.time);
            SessionLog.Log("answer", objectId, outcome.ToString());
            switch (outcome)
            {
                case SubmitOutcome.Correct:
                    hud.ShowFeedback(_instructor.AfterCorrect(q) + "\n[Enter] continue", Good);
                    break;
                case SubmitOutcome.Wrong:
                    hud.ShowFeedback(_instructor.AfterWrong(objectId, q.maxAttempts - Quiz.CurrentResult.wrongAttempts), Bad);
                    break;
                case SubmitOutcome.Revealed:
                    hud.ShowFeedback(_instructor.AfterRevealed(q) + "\n[Enter] continue", Bad);
                    break;
            }
            return outcome;
        }

        public string RequestHint()
        {
            if (Mode != AppMode.Quiz || Quiz == null) return null;
            string h = Quiz.RequestHint();
            SessionLog.Log("hint", Quiz.Current != null ? Quiz.Current.correctObjectId : "", h ?? "none");
            hud.ShowFeedback(h != null ? "Hint: " + h : _instructor.NoMoreHints(), Neutral);
            return h;
        }

        public void SkipQuestion()
        {
            if (Mode != AppMode.Quiz || Quiz == null) return;
            var q = Quiz.Current;
            if (Quiz.Skip(Time.time)) hud.ShowFeedback("Skipped. " + _instructor.AfterRevealed(q) + "\n[Enter] continue", Bad);
        }

        public void ContinueQuiz()
        {
            if (Mode != AppMode.Quiz || Quiz == null || !Quiz.AwaitingContinue) return;
            Quiz.Continue(Time.time);
            if (Quiz.Complete) { FinishQuiz(); return; }
            ShowTask();
            hud.ShowFeedback("", Neutral);
        }

        void FinishQuiz()
        {
            var score = _scoring.ScoreIdentification(Quiz.Results, Equipment.NameOf);
            LastReport = SessionReport.Build(_quizSet.title, _startedUtc, Time.time - _startTime, Quiz.Results, score, _scoring.PassThreshold, Equipment.NameOf);
            LastReportPath = null;
            if (saveReports)
            {
                try { LastReportPath = LastReport.Save(reportDirectory); }
                catch (Exception e) { Debug.LogWarning("Could not save report: " + e.Message); }
            }
            SessionLog.Log("quiz_complete", "", "score=" + LastReport.score.ToString("F1"));
            RecordModule(ModuleReport.FromIdentification(_quizSet.moduleId, LastReport, score), false);
            SetMode(AppMode.Results);
            hud.SetTask(null);
            hud.ShowFeedback("", Neutral);
            string body = _instructor.SessionSummary(LastReport) + "\n\nTime: " + LastReport.durationSeconds.ToString("F0") + " s   Errors: "
                          + LastReport.totalErrors + "   Hints: " + LastReport.totalHints + "\n\n"
                          + string.Join("\n", LastReport.feedback)
                          + (LastReportPath != null ? "\n\nReport saved: " + LastReportPath : "")
                          + "\n\nPrototype scoring: weights are provisional, not clinically validated.\n\n[Enter] return to the room";
            hud.ShowOverlay("RESULTS", body);
        }

        // ---------- event handlers ----------
        void HandleSelected(InteractableObject io)
        {
            if (Mode == AppMode.Quiz) SubmitSelection(io.objectId);
            else if (Mode == AppMode.Explore) hud.ShowFeedback("Selected: " + io.displayName, Neutral);
            else if (Mode == AppMode.Monitoring && io.objectId == "monitor_patient_a01") OpenReportPrompt();
        }

        void HandleInspected(InteractableObject io)
        {
            if (Mode == AppMode.Quiz)
            {
                hud.ShowInfo("Inspection is turned off during identification tasks.");
                return;
            }
            string s = io.displayName.ToUpperInvariant() + "\n" + io.category + "\n\n" + io.description + "\n\nTraining use: " + io.trainingUse;
            if (!string.IsNullOrEmpty(io.labelText)) s += "\n\nLABEL:\n" + io.labelText;
            if (io.isPlaceholder) s += "\n\n[Placeholder model - replace with licensed asset]";
            hud.ShowInfo(s);
        }

        void HandleHover(InteractableObject io)
        {
            if (hud == null) return;
            if (io == null) { hud.SetHover(""); hud.ShowInfo(null); return; }
            string actions = "[Click] Select";
            if (io.canInspect && Mode != AppMode.Quiz) actions = "[E] Inspect   " + actions;
            if (io.canGrab) actions += "   [G] Grab";
            hud.SetHover(Mode == AppMode.Quiz ? actions : io.displayName + "   " + actions);
            if (hud.InfoVisible) hud.ShowInfo(null);
        }
    }
}
