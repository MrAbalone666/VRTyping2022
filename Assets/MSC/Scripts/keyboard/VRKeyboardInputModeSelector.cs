using UnityEngine;

namespace VRTyping.Keyboard
{
    public enum VRKeyboardInputMode
    {
        Press,
        Swipe,
        StickTap,
        Dwell,
        // HandTouch：只使用左右手食指指尖作为按键触碰点，适合普通手部点击。
        HandTouch,
        // HandTouch10：十个手指都生成触碰探针，任意手指都可以按键。
        HandTouch10,
    }

    public class VRKeyboardInputModeSelector : MonoBehaviour
    {
        [SerializeField] VRKeyboardInputMode m_InputMode = VRKeyboardInputMode.Press;

        [Header("Input Components")]
        [SerializeField] VRKeyboardController m_PressInput;
        [SerializeField] VRKeyboardSwipeInput m_SwipeInput;
        [SerializeField] VRKeyboardRayProbeFollower m_RayProbeInput;
        [SerializeField] VRKeyboardStickProbeFollower m_StickTapInput;
        // 手部输入统一由 VRKeyboardHandProbeFollower 管理，模式切换这里只负责启停和十指开关。
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
            // UI 或调试按钮可以调用这个方法切到“左右食指触碰”模式。
            SetInputMode(VRKeyboardInputMode.HandTouch);
        }

        public void SetHandTouch10Mode()
        {
            // UI 或调试按钮可以调用这个方法切到“十指触碰”模式。
            SetInputMode(VRKeyboardInputMode.HandTouch10);
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
            // 手部触碰最终仍然是“离散按键按下”，所以需要保留 VRKeyboardController 的按键事件处理。
            var useDiscreteKeyPress =
                m_InputMode == VRKeyboardInputMode.Press ||
                m_InputMode == VRKeyboardInputMode.StickTap ||
                m_InputMode == VRKeyboardInputMode.Dwell ||
                m_InputMode == VRKeyboardInputMode.HandTouch ||
                m_InputMode == VRKeyboardInputMode.HandTouch10;

            if (m_PressInput != null)
                m_PressInput.enabled = useDiscreteKeyPress;

            if (m_SwipeInput != null)
                m_SwipeInput.enabled = m_InputMode == VRKeyboardInputMode.Swipe;

            if (m_RayProbeInput != null)
            {
                // HandTouch/HandTouch10 使用指尖探针，不再使用控制器射线探针，避免两套输入同时按键。
                var enableRayProbe = m_InputMode != VRKeyboardInputMode.StickTap &&
                    m_InputMode != VRKeyboardInputMode.HandTouch &&
                    m_InputMode != VRKeyboardInputMode.HandTouch10;
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
            {
                // HandTouch 与 HandTouch10 共用同一个脚本，只通过 SetUseAllFingerTips 区分“食指”还是“十指”。
                var enableHandTouch = m_InputMode == VRKeyboardInputMode.HandTouch ||
                    m_InputMode == VRKeyboardInputMode.HandTouch10;
                m_HandTouchInput.SetUseAllFingerTips(m_InputMode == VRKeyboardInputMode.HandTouch10);
                m_HandTouchInput.enabled = enableHandTouch;
            }
        }
    }
}
