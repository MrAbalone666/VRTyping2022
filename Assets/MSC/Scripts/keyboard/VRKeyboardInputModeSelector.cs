using UnityEngine;

namespace VRTyping.Keyboard
{
    // VR 键盘支持的输入模式。
    public enum VRKeyboardInputMode
    {
        // 射线/探针直接按下单个按键。
        Press,
        // 沿键盘滑动并识别单词。
        Swipe,
        // 使用手柄或手部方向生成的虚拟 stick 点击按键。
        StickTap,
        // 射线停留在按键上足够久后自动输入。
        Dwell,
    }

    // 输入模式选择器：负责启用当前模式需要的组件，并关闭其他输入组件。
    public class VRKeyboardInputModeSelector : MonoBehaviour
    {
        [SerializeField]
        // 当前选择的输入模式。
        VRKeyboardInputMode m_InputMode = VRKeyboardInputMode.Press;

        [Header("Input Components")]
        [SerializeField]
        // 离散按键输入控制器，Press/StickTap/Dwell 最终都通过它处理 onPressed。
        VRKeyboardController m_PressInput;

        [SerializeField]
        // Swipe 模式下记录轨迹并提交识别结果。
        VRKeyboardSwipeInput m_SwipeInput;

        [SerializeField]
        // 射线探针，负责 Press、Swipe、Dwell 三种射线相关输入。
        VRKeyboardRayProbeFollower m_RayProbeInput;

        [SerializeField]
        // StickTap 模式下使用的 stick 探针。
        VRKeyboardStickProbeFollower m_StickTapInput;

        public VRKeyboardInputMode currentInputMode => m_InputMode;

        void Reset()
        {
            // 自动寻找常用输入组件，减少 Inspector 手动拖引用。
            if (m_PressInput == null)
                m_PressInput = GetComponent<VRKeyboardController>();

            if (m_SwipeInput == null)
                m_SwipeInput = GetComponent<VRKeyboardSwipeInput>();

            if (m_RayProbeInput == null)
                m_RayProbeInput = FindObjectOfType<VRKeyboardRayProbeFollower>(true);

            if (m_StickTapInput == null)
                m_StickTapInput = FindObjectOfType<VRKeyboardStickProbeFollower>(true);
        }

        void OnEnable()
        {
            // 启用时根据当前模式刷新组件开关。
            ApplyMode();
        }

        void OnValidate()
        {
            // Inspector 中切换模式时立刻预览组件启用状态。
            ApplyMode();
        }

        public void SetInputMode(VRKeyboardInputMode inputMode)
        {
            // UI 按钮或其他脚本可通过这个入口切换输入模式。
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

        void ApplyMode()
        {
            // Press、StickTap、Dwell 都是离散按键输入，最后都会触发 VRKeyboardKey.onPressed。
            var useDiscreteKeyPress =
                m_InputMode == VRKeyboardInputMode.Press ||
                m_InputMode == VRKeyboardInputMode.StickTap ||
                m_InputMode == VRKeyboardInputMode.Dwell;

            if (m_PressInput != null)
                m_PressInput.enabled = useDiscreteKeyPress;

            if (m_SwipeInput != null)
                m_SwipeInput.enabled = m_InputMode == VRKeyboardInputMode.Swipe;

            if (m_RayProbeInput != null)
            {
                // StickTap 使用自己的 stick 探针，不需要射线探针。
                var enableRayProbe = m_InputMode != VRKeyboardInputMode.StickTap;
                m_RayProbeInput.enabled = enableRayProbe;

                if (enableRayProbe)
                {
                    // 对射线探针来说，普通 Press 和其他离散输入都归到 Press/Dwell/Swipe 三类。
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
        }
    }
}
