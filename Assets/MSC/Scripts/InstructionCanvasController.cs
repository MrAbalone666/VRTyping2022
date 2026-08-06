using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRTyping.Keyboard;


namespace VRTyping.Tests
{
    
    public class InstructionCanvasController : MonoBehaviour
    {
        enum FlowStage
        {
            Introduction,
            MethodInstruction,
            PracticeChoice,
            PracticeTrial,
            FormalReady,
            FormalTrial,
            FormalResult,
            Completed,
        }

        static readonly VRKeyboardInputMode[] MethodOrder =
        {
            VRKeyboardInputMode.Press,
            VRKeyboardInputMode.Swipe,
            VRKeyboardInputMode.StickTap,
            VRKeyboardInputMode.Dwell,
            VRKeyboardInputMode.HandTouch,
            VRKeyboardInputMode.HandTouch10,
        };

        const string IntroductionText =
            "<b>VR Typing Study</b>\n\n" +
            "You will test six virtual keyboard input methods.\n\nEnter the displayed text as quickly and accurately as possible. Each formal test lasts 120 seconds.\n\nYou may stop at any time if you feel uncomfortable. ";

        const string PracticeQuestionText =
            "<b>Would you like to practise first?</b>\n\n";

        [Header("Pages")]
        public GameObject m_InstructionCanvas;
        public GameObject m_MethodInstruction;
        public GameObject m_PracticeOrNot;

        [Header("Text")]
        public TMP_Text m_IntroductionText;
        public TMP_Text m_MethodInstructionText;
        public TMP_Text m_PracticeQuestionText;

        [Header("Buttons")]
        public Button m_IntroductionContinueButton;
        public Button m_MethodContinueButton;
        public Button m_PracticeYesButton;
        public Button m_PracticeNoButton;

        [Header("Existing Trial Controls")]
        public TypingTestSession m_TypingSession;
        public Toggle m_IsPracticeToggle;
        public VRKeyboardInputModeSelector m_InputModeSelector;
        public TMP_Dropdown m_InputModeDropdown;

        bool m_ListenersAdded;
        int m_CurrentMethodIndex;
        FlowStage m_FlowStage;
        Coroutine m_ResultDelayCoroutine;

        const float ResultDisplaySeconds = 5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AttachToTypingScene()
        {
            if (FindObjectOfType<InstructionCanvasController>(true) != null)
                return;

            var session = FindObjectOfType<TypingTestSession>(true);
            if (session == null || FindNamedGameObject("InstructionCanvas") == null)
                return;

            session.gameObject.AddComponent<InstructionCanvasController>();
        }

        void Start()
        {
            InitializeCurrentMethod();
            ConfigureContent();
            AddListeners();
            ShowIntroduction();
        }

        void OnDestroy()
        {
            if (m_ResultDelayCoroutine != null)
                StopCoroutine(m_ResultDelayCoroutine);

            RemoveListeners();
        }


        void ConfigureContent()
        {
            if (m_IntroductionText != null)
                m_IntroductionText.text = IntroductionText;
            if (m_PracticeQuestionText != null)
                m_PracticeQuestionText.text = PracticeQuestionText;


            SetButtonLabel(m_IntroductionContinueButton, "Continue");
            SetButtonLabel(m_MethodContinueButton, "Continue");
            SetButtonLabel(m_PracticeYesButton, "Yes");
            SetButtonLabel(m_PracticeNoButton, "No");

            RefreshMethodInstruction(GetCurrentInputMode());
        }

        

        void AddListeners()
        {
            if (m_ListenersAdded)
                return;

            if (m_IntroductionContinueButton != null)
                m_IntroductionContinueButton.onClick.AddListener(ShowMethodInstruction);
            if (m_MethodContinueButton != null)
                m_MethodContinueButton.onClick.AddListener(HandleMethodContinue);
            if (m_PracticeYesButton != null)
                m_PracticeYesButton.onClick.AddListener(SelectPractice);
            if (m_PracticeNoButton != null)
                m_PracticeNoButton.onClick.AddListener(SelectFormalTest);
            if (m_InputModeDropdown != null)
                m_InputModeDropdown.onValueChanged.AddListener(HandleInputModeDropdownChanged);
            if (m_TypingSession != null)
                m_TypingSession.TrialFinished += HandleTrialFinished;

            m_ListenersAdded = true;
        }

        void RemoveListeners()
        {
            if (!m_ListenersAdded)
                return;

            if (m_IntroductionContinueButton != null)
                m_IntroductionContinueButton.onClick.RemoveListener(ShowMethodInstruction);
            if (m_MethodContinueButton != null)
                m_MethodContinueButton.onClick.RemoveListener(HandleMethodContinue);
            if (m_PracticeYesButton != null)
                m_PracticeYesButton.onClick.RemoveListener(SelectPractice);
            if (m_PracticeNoButton != null)
                m_PracticeNoButton.onClick.RemoveListener(SelectFormalTest);
            if (m_InputModeDropdown != null)
                m_InputModeDropdown.onValueChanged.RemoveListener(HandleInputModeDropdownChanged);
            if (m_TypingSession != null)
                m_TypingSession.TrialFinished -= HandleTrialFinished;

            m_ListenersAdded = false;
        }

        public void ShowIntroduction()
        {
            m_FlowStage = FlowStage.Introduction;
            SetInputModeDropdownInteractable(true);
            SetVisiblePage(m_InstructionCanvas);
            DeactivateTypingInput();
        }

        public void ShowMethodInstruction()
        {
            m_FlowStage = FlowStage.MethodInstruction;
            SetInputModeDropdownInteractable(true);
            RefreshMethodInstruction(GetCurrentInputMode());
            HideTypingResult();
            SetVisiblePage(m_MethodInstruction);
            DeactivateTypingInput();
        }

        public void ShowPracticeQuestion()
        {
            m_FlowStage = FlowStage.PracticeChoice;
            SetInputModeDropdownInteractable(true);
            SetVisiblePage(m_PracticeOrNot);
            DeactivateTypingInput();
        }

        public void SelectPractice()
        {
            ApplyPracticeChoice(true);
        }

        public void SelectFormalTest()
        {
            ApplyPracticeChoice(false);
        }

        public void ApplyPracticeChoice(bool isPractice)
        {
            SynchronizeCurrentMethodFromSelector();
            m_FlowStage = isPractice ? FlowStage.PracticeTrial : FlowStage.FormalTrial;
            SetInputModeDropdownInteractable(false);

            // TypingTestSession is the single mode-switch entry point: it synchronizes
            // ResultOutput, IsPracticeToggle, the active sentence set and the trial reset.
            if (m_TypingSession != null)
            {
                m_TypingSession.SetPracticeMode(isPractice);
            }
            else if (m_IsPracticeToggle != null)
            {
                m_IsPracticeToggle.isOn = isPractice;
            }

            HideAllPages();

            if (m_TypingSession != null && m_TypingSession.m_InputField != null)
                m_TypingSession.m_InputField.ActivateInputField();
        }

        void HandleInputModeDropdownChanged(int optionIndex)
        {
            if (optionIndex < 0 || optionIndex > (int)VRKeyboardInputMode.HandTouch10)
                return;

            var mode = (VRKeyboardInputMode)optionIndex;
            m_CurrentMethodIndex = FindMethodIndex(mode);
            RefreshMethodInstruction(mode);
        }

        void HandleMethodContinue()
        {
            if (m_FlowStage == FlowStage.FormalReady)
            {
                StartPreparedFormalTrial();
                return;
            }

            ShowPracticeQuestion();
        }

        void HandleTrialFinished(bool wasPractice)
        {
            if (wasPractice)
            {
                ShowFormalReadyPrompt();
                return;
            }

            m_FlowStage = FlowStage.FormalResult;
            SetInputModeDropdownInteractable(false);
            DeactivateTypingInput();

            if (m_ResultDelayCoroutine != null)
                StopCoroutine(m_ResultDelayCoroutine);
            m_ResultDelayCoroutine = StartCoroutine(AdvanceAfterResultDelay());
        }

        IEnumerator AdvanceAfterResultDelay()
        {
            yield return new WaitForSecondsRealtime(ResultDisplaySeconds);
            m_ResultDelayCoroutine = null;
            AdvanceToNextMethod();
        }

        void ShowFormalReadyPrompt()
        {
            // Reset the same input method with the formal sentence set. The test timer
            // still waits for the first real input, and the instruction page blocks input
            // until the participant explicitly continues.
            m_FlowStage = FlowStage.FormalReady;
            SetInputModeDropdownInteractable(false);

            if (m_TypingSession != null)
                m_TypingSession.SetPracticeMode(false);
            else if (m_IsPracticeToggle != null)
                m_IsPracticeToggle.SetIsOnWithoutNotify(false);

            RefreshMethodInstruction(GetCurrentInputMode());
            if (m_MethodInstructionText != null)
            {
                m_MethodInstructionText.text +=
                    "\n\n<b>Practice complete.</b> Select Continue when you are ready to begin the formal test for this method.";
            }

            SetVisiblePage(m_MethodInstruction);
            DeactivateTypingInput();
        }

        void StartPreparedFormalTrial()
        {
            m_FlowStage = FlowStage.FormalTrial;
            SetInputModeDropdownInteractable(false);
            HideAllPages();

            if (m_TypingSession != null && m_TypingSession.m_InputField != null)
                m_TypingSession.m_InputField.ActivateInputField();
        }

        void AdvanceToNextMethod()
        {
            if (m_CurrentMethodIndex + 1 >= MethodOrder.Length)
            {
                m_FlowStage = FlowStage.Completed;
                SetInputModeDropdownInteractable(false);
                DeactivateTypingInput();
                return;
            }

            m_CurrentMethodIndex++;
            ApplyInputMode(MethodOrder[m_CurrentMethodIndex]);
            ShowMethodInstruction();
        }

        void InitializeCurrentMethod()
        {
            var mode = GetCurrentInputMode();
            m_CurrentMethodIndex = FindMethodIndex(mode);
            ApplyInputMode(mode);
        }

        void SynchronizeCurrentMethodFromSelector()
        {
            var mode = GetCurrentInputMode();
            m_CurrentMethodIndex = FindMethodIndex(mode);
            ApplyInputMode(mode);
        }

        void ApplyInputMode(VRKeyboardInputMode mode)
        {
            if (m_InputModeSelector != null && m_InputModeSelector.currentInputMode != mode)
                m_InputModeSelector.SetInputMode(mode);

            if (m_InputModeDropdown != null)
            {
                m_InputModeDropdown.SetValueWithoutNotify((int)mode);
                m_InputModeDropdown.RefreshShownValue();
            }

            RefreshMethodInstruction(mode);
        }

        static int FindMethodIndex(VRKeyboardInputMode mode)
        {
            for (var i = 0; i < MethodOrder.Length; i++)
            {
                if (MethodOrder[i] == mode)
                    return i;
            }

            return 0;
        }

        VRKeyboardInputMode GetCurrentInputMode()
        {
            if (m_InputModeSelector != null)
                return m_InputModeSelector.currentInputMode;

            if (m_InputModeDropdown != null &&
                m_InputModeDropdown.value >= 0 &&
                m_InputModeDropdown.value <= (int)VRKeyboardInputMode.HandTouch10)
            {
                return (VRKeyboardInputMode)m_InputModeDropdown.value;
            }

            return VRKeyboardInputMode.Press;
        }

        void RefreshMethodInstruction(VRKeyboardInputMode mode)
        {
            if (m_MethodInstructionText == null)
                return;

            switch (mode)
            {
                case VRKeyboardInputMode.Swipe:
                    m_MethodInstructionText.text =
                        "<b>Current method: Swipe</b>\n\n" +
                        "Aim the controller ray at the first letter, hold the trigger and move continuously across the letters of the word. Release the trigger to finish the gesture. The recogniser displays candidate words. Use the right thumbstick to highlight a candidate and press the right trigger to confirm it.";
                    break;

                case VRKeyboardInputMode.StickTap:
                    m_MethodInstructionText.text =
                        "<b>Current method: Stick Tap</b>\n\n" +
                        "Position the virtual stick above a key and tap it with the tip of the stick. Lift the stick away before tapping the next key.";
                    break;

                case VRKeyboardInputMode.Dwell:
                    m_MethodInstructionText.text =
                        "<b>Current method: Dwell</b>\n\n" +
                        "Aim the controller ray at a key and keep it steady until the key is activated. Move the ray to the next key after activation. No trigger press is required.";
                    break;

                case VRKeyboardInputMode.HandTouch:
                    m_MethodInstructionText.text =
                        "<b>Current method: Two-Finger Hand Touch</b>\n\n" +
                        "Use your left and right index fingertips to touch the virtual keys. Lift each finger away before touching the next key.";
                    break;

                case VRKeyboardInputMode.HandTouch10:
                    m_MethodInstructionText.text =
                        "<b>Current method: Ten-Finger Hand Touch 10</b>\n\n" +
                        "Use any of your ten fingertips to touch the virtual keys. Lift each finger away before touching the next key.";
                    break;

                default:
                    m_MethodInstructionText.text =
                        "<b>Current method: Press</b>\n\n" +
                        "Aim the controller ray at a key and press the trigger to enter it. Use Space to add a space and Backspace to delete a character.";
                    break;
            }
        }

        void SetVisiblePage(GameObject visiblePage)
        {
            if (m_InstructionCanvas != null)
                m_InstructionCanvas.SetActive(m_InstructionCanvas == visiblePage);
            if (m_MethodInstruction != null)
                m_MethodInstruction.SetActive(m_MethodInstruction == visiblePage);
            if (m_PracticeOrNot != null)
                m_PracticeOrNot.SetActive(m_PracticeOrNot == visiblePage);
        }

        void HideAllPages()
        {
            SetVisiblePage(null);
        }

        void HideTypingResult()
        {
            if (m_TypingSession != null && m_TypingSession.m_ResultPanel != null)
                m_TypingSession.m_ResultPanel.gameObject.SetActive(false);
        }

        void DeactivateTypingInput()
        {
            if (m_TypingSession != null && m_TypingSession.m_InputField != null)
                m_TypingSession.m_InputField.DeactivateInputField();
        }

        void SetInputModeDropdownInteractable(bool interactable)
        {
            if (m_InputModeDropdown != null)
                m_InputModeDropdown.interactable = interactable;
        }

        static GameObject FindNamedGameObject(string objectName)
        {
            var transforms = FindObjectsOfType<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var candidate = transforms[i];
                if (candidate != null && candidate.name == objectName && candidate.gameObject.scene.IsValid())
                    return candidate.gameObject;
            }

            return null;
        }



        static void SetButtonLabel(Button button, string label)
        {
            if (button == null)
                return;

            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
                text.text = label;
        }
       
    }
}
