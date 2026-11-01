using MedSimXR.Assessment;
using UnityEngine;

namespace MedSimXR.Training
{
    /// <summary>
    /// Controller-friendly entry points. The keyboard path and the VR path both end up in the same methods,
    /// so the modules behave identically. Number choices (menu, quiz answers, report prompt) are made with a cursor.
    /// </summary>
    public partial class TrainingManager
    {
        public bool VrActive { get; set; }
        public int VrCursor { get; private set; }

        static readonly string[] ReportKeys = { "hr", "spo2", "rr", "bp", "temp" };

        /// <summary>Number of selectable options on screen right now, or 0 when no option list is showing.</summary>
        public int VrOptionCount
        {
            get
            {
                switch (Mode)
                {
                    case AppMode.Menu: return 8;
                    case AppMode.Knowledge:
                        return Knowledge != null && !Knowledge.AwaitingContinue && Knowledge.Current != null ? Knowledge.Current.options.Length : 0;
                    case AppMode.Monitoring: return _awaitingParam ? ReportKeys.Length + 1 : 0;   // last entry = cancel
                    default: return 0;
                }
            }
        }

        public void VrOptionMove(int delta)
        {
            int n = VrOptionCount;
            if (n == 0) return;
            VrCursor = ((VrCursor + delta) % n + n) % n;
        }

        /// <summary>Menu entry points shared by the number keys and the VR cursor.</summary>
        public void ChooseMenu(int number)
        {
            if (Mode != AppMode.Menu) return;
            switch (number)
            {
                case 1: StartIdentification(); break;
                case 2: StartProcedure(ProgressStore.ModuleB); break;
                case 3: StartMonitoring(); break;
                case 4: StartProcedure(ProgressStore.ModuleD); break;
                case 5: StartKnowledge(); break;
                case 6: StartAdvancedIdentification(); break;
                case 7: ShowDashboard(); break;
                case 8: CloseMenu(); break;
            }
        }

        /// <summary>A button: confirms the highlighted option, continues, or leaves a results screen. Returns false if it did nothing.</summary>
        public bool VrConfirm()
        {
            switch (Mode)
            {
                case AppMode.Menu:
                    ChooseMenu(VrCursor + 1);
                    return true;
                case AppMode.Quiz:
                    if (Quiz != null && Quiz.AwaitingContinue) { ContinueQuiz(); return true; }
                    return false;
                case AppMode.Knowledge:
                    if (Knowledge == null) return false;
                    if (Knowledge.AwaitingContinue) ContinueKnowledge();
                    else AnswerKnowledge(VrCursor);
                    return true;
                case AppMode.Monitoring:
                    if (!_awaitingParam) return false;
                    if (VrCursor >= ReportKeys.Length) { _awaitingParam = false; hud.ShowFeedback("Cancelled.", Neutral); }
                    else ReportParam(ReportKeys[VrCursor]);
                    return true;
                case AppMode.Results:
                    ReturnToRoom();
                    return true;
            }
            return false;
        }

        public void VrHint()
        {
            switch (Mode)
            {
                case AppMode.Quiz: RequestHint(); break;
                case AppMode.Procedure: RequestProcedureHint(); break;
                case AppMode.Knowledge: RequestKnowledgeHint(); break;
            }
        }

        /// <summary>Left Y: open or close the training menu from the free-exploration view.</summary>
        public void VrMenu()
        {
            if (Mode == AppMode.Explore) OpenMenu();
            else if (Mode == AppMode.Menu) CloseMenu();
        }

        public void VrQuit() { CancelModule(); }
    }
}
