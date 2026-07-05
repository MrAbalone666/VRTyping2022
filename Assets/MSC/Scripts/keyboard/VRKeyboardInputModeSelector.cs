using UnityEngine;

namespace VRTyping.Keyboard
{
    public enum VRKeyboardInputMode
    {
        Press,
        Swipe,
        StickTap,
        Dwell,
        HandTouch,
    }

    public class VRKeyboardInputModeSelector : MonoBehaviour
    {
        [SerializeField] VRKeyboardInputMode m_InputMode = VRKeyboardInputMode.Press;

        [Header("Input Components")]
        [SerializeField] VRKeyboardController m_PressInput;
        [SerializeField] VRKeyboardSwipeInput m_SwipeInput;
        [SerializeField] VRKeyboardRayProbeFollower m_RayProbeInput;
        [SerializeField] VRKeyboardStickProbeFollower m_StickTapInput;
        [SerializeField] VRKeyboardHandProbeFollower m_HandTouchInput;

        public VRKeyboardInputMode currentInputMode => m_InputMode;

        void Reset()
        {
            CacheInputComponents();
        }

        void OnEnable()
        {
            CacheInputComponents();
            ApplyMode();
        }

        void OnValidate()
        {
            CacheInputComponents();
            ApplyMode();
        }

        public void SetInputMode(VRKeyboardInputMode inputMode)
        {
            m_InputMode = inputMode;
            ApplyMode();
        }

        public void SetPressMode()
        {
            SetInputMode(VRKeyboardInputMode.Press);
        }

        public void SetSwipeMode()
        {
            SetInputMode(VRKeyboardInputMode.Swipe);
        }

        public void SetStickTapMode()
        {
            SetInputMode(VRKeyboardInputMode.StickTap);
        }

        public void SetDwellMode()
        {
            SetInputMode(VRKeyboardInputMode.Dwell);
        }

        public void SetHandTouchMode()
        {
            SetInputMode(VRKeyboardInputMode.HandTouch);
        }

        void CacheInputComponents()
        {
            if (m_PressInput == null)
                m_PressInput = GetComponent<VRKeyboardController>();

            if (m_SwipeInput == null)
                m_SwipeInput = GetComponent<VRKeyboardSwipeInput>();

            if (m_RayProbeInput == null)
                m_RayProbeInput = FindObjectOfType<VRKeyboardRayProbeFollower>(true);

            if (m_StickTapInput == null)
                m_StickTapInput = FindObjectOfType<VRKeyboardStickProbeFollower>(true);

            if (m_HandTouchInput == null)
                m_HandTouchInput = FindObjectOfType<VRKeyboardHandProbeFollower>(true);
        }

        void ApplyMode()
        {
            var useDiscreteKeyPress =
                m_InputMode == VRKeyboardInputMode.Press ||
                m_InputMode == VRKeyboardInputMode.StickTap ||
                m_InputMode == VRKeyboardInputMode.Dwell ||
                m_InputMode == VRKeyboardInputMode.HandTouch;

            if (m_PressInput != null)
                m_PressInput.enabled = useDiscreteKeyPress;

            if (m_SwipeInput != null)
                m_SwipeInput.enabled = m_InputMode == VRKeyboardInputMode.Swipe;

            if (m_RayProbeInput != null)
            {
                var enableRayProbe = m_InputMode != VRKeyboardInputMode.StickTap &&
                    m_InputMode != VRKeyboardInputMode.HandTouch;
                m_RayProbeInput.enabled = enableRayProbe;

                if (enableRayProbe)
                {
                    var rayMode = m_InputMode == VRKeyboardInputMode.Swipe
                        ? VRKeyboardInputMode.Swipe
                        : m_InputMode == VRKeyboardInputMode.Dwell
                            ? VRKeyboardInputMode.Dwell
                            : VRKeyboardInputMode.Press;
                    m_RayProbeInput.SetInputMode(rayMode);
                }
            }

            if (m_StickTapInput != null)
                m_StickTapInput.enabled = m_InputMode == VRKeyboardInputMode.StickTap;

            if (m_HandTouchInput != null)
                m_HandTouchInput.enabled = m_InputMode == VRKeyboardInputMode.HandTouch;
        }
    }
}
