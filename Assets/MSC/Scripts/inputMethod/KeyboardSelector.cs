//using UnityEngine;
//using VRTyping.Keyboard;

//public class KeyboardSelector : MonoBehaviour
//{
//    public enum InputMode//输入方式
//    {
//        Press,
//        Swipe,
//        StickTap,
//        Dwell,
//    }

//    public InputMode m_InputMode = InputMode.Press;//默认输入方式为按压

//    public VRKeyboardController m_PressInput; // 普通按键输入
//    public VRKeyboardSwipeInput m_SwipeInput; // 滑动输入
//    public VRKeyboardRayProbeFollower m_RayProbeInput; // 射线探针
//    public VRKeyboardStickProbeFollower m_StickTapInput; // 小棒探针

//    public InputMode currentInputMode => m_InputMode;//当前输入方式

//    // Start is called once before the first execution of Update after the MonoBehaviour is created
//    void Start()
//    {
//        ApplyMode();
//    }

//    // Update is called once per frame
//    void Update()
//    {

//    }

//    //void Reset()
//    //{
//    //    // Reset 在添加组件或点击 Inspector Reset 时调用，用来自动寻找默认引用
//    //    if (m_PressInput == null)
//    //        m_PressInput = GetComponent<VRKeyboardController>();

//    //    if (m_SwipeInput == null)
//    //        m_SwipeInput = GetComponent<VRKeyboardSwipeInput>();

//    //    if (m_RayProbeInput == null)
//    //        m_RayProbeInput = FindObjectOfType<VRKeyboardRayProbeFollower>(true);

//    //    if (m_StickTapInput == null)
//    //        m_StickTapInput = FindObjectOfType<VRKeyboardStickProbeFollower>(true);
//    //}

//    public void SetInputMode(InputMode inputMode)//切换输入方式（外部调用）
//    {
//        // 外部统一通过这个方法切换输入模式
//        m_InputMode = inputMode;
//        ApplyMode();
//    }

//    public void SetPressMode()//press
//    {
//        SetInputMode(InputMode.Press);
//    }

//    public void SetSwipeMode()//swipe
//    {
//        SetInputMode(InputMode.Swipe);
//    }

//    public void SetStickTapMode()//sticktap
//    {
//        SetInputMode(InputMode.StickTap);
//    }

//    public void SetDwellMode()//dwell
//    {
//        SetInputMode(InputMode.Dwell);
//    }

//    void ApplyMode()//应用输入方式
//    {
//        // Press / StickTap / Dwell 最终都是触发单个按键的 onPressed，
//        // 所以它们都需要 VRKeyboardController 来把按键转换成文字。
//        var useDiscreteKeyPress =
//            m_InputMode == InputMode.Press ||
//            m_InputMode == InputMode.StickTap ||
//            m_InputMode == InputMode.Dwell;

//        if (m_PressInput != null)
//            m_PressInput.enabled = useDiscreteKeyPress;

//        // Swipe 模式使用自己的输入脚本，普通按键输入在 Swipe 时关闭
//        if (m_SwipeInput != null)
//            m_SwipeInput.enabled = m_InputMode == InputMode.Swipe;

//        if (m_RayProbeInput != null)
//        {
//            // StickTap 使用小棒探针，所以关闭射线探针；
//            // 其他模式都需要射线探针。
//            var enableRayProbe = m_InputMode != InputMode.StickTap;
//            m_RayProbeInput.enabled = enableRayProbe;

//            if (enableRayProbe)
//            {
//                // RayProbe 内部只需要知道自己当前是 Press / Swipe / Dwell 行为。
//                // StickTap 不会传给 RayProbe，因为 StickTap 时 RayProbe 已关闭。
//                var rayMode = m_InputMode == InputMode.Swipe
//                    ? InputMode.Swipe
//                    : m_InputMode == InputMode.Dwell
//                        ? InputMode.Dwell
//                        : InputMode.Press;

//                //m_RayProbeInput.SetInputMode(rayMode);
//            }
//        }

//        // StickTap 模式只启用小棒探针
//        if (m_StickTapInput != null)
//            m_StickTapInput.enabled = m_InputMode == InputMode.StickTap;
//    }
//}
