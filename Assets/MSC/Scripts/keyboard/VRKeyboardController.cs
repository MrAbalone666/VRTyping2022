using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace VRTyping.Keyboard
{
    // 监听所有子按键的按下事件，并把按键输入写入目标 TMP 输入框。
    public class VRKeyboardController : MonoBehaviour
    {

        // 键盘最终要输入文字的目标输入框。
        public TMP_InputField m_OutputField;

        // 为 true 时，Tab 键输入真实的 \t；为 false 时，输入指定数量的空格。
        public bool m_UseTabCharacter;


        [Min(1)]
        // 当不使用真实 Tab 字符时，Tab 键会转换成几个空格。
        public int m_TabSpaces = 4;

        // CapsLock 当前是否开启，会影响字母大小写。
        public bool m_CapsLockEnabled;

        // Shift 当前是否开启；输入一个可打印字符后会由 TextComposer 自动关闭。
        public bool m_ShiftEnabled;

        // 记录每个按键注册过的监听器，方便禁用控制器时完整移除，避免重复绑定。
        readonly Dictionary<VRKeyboardKey, UnityAction> m_KeyListeners = new Dictionary<VRKeyboardKey, UnityAction>();
        VRKeyboardSwipeInput m_SwipeInput;

        // 外部可读取当前输入框里的文字。
        public string currentText => VRKeyboardTextComposer.GetText(m_OutputField);

        void OnEnable()
        {
            // 控制器启用时，扫描子物体中的键并绑定事件。
            m_SwipeInput = GetComponent<VRKeyboardSwipeInput>();
            RegisterKeys();
        }

        void OnDisable()
        {
            // 控制器禁用时移除事件，避免对象反复启用后同一个按键触发多次。
            UnregisterKeys();
        }

        public void ClearText()
        {
            // 对外提供清空输入框的接口。
            VRKeyboardTextComposer.ClearText(m_OutputField);
        }

        void RegisterKeys()
        {
            UnregisterKeys();

            // 包含 inactive 子物体，保证隐藏或暂时禁用的按键恢复后也能被控制器管理。
            var keys = GetComponentsInChildren<VRKeyboardKey>(true);
            foreach (var key in keys)
            {
                // 为每个按键创建独立回调，把具体按键对象传给 HandleKeyPressed。
                UnityAction handler = () => HandleKeyPressed(key);
                key.onPressed.AddListener(handler);
                m_KeyListeners[key] = handler;
            }
        }

        void UnregisterKeys()
        {
            // 按注册时保存的 UnityAction 精确移除监听器。
            foreach (var pair in m_KeyListeners)
            {
                if (pair.Key != null)
                    pair.Key.onPressed.RemoveListener(pair.Value);
            }

            m_KeyListeners.Clear();
        }

        void HandleKeyPressed(VRKeyboardKey key)
        {
            Debug.Log($"Controller received key: {key.name}");

            // 从按键对象解析出逻辑 ID，例如 A、Back、Space、Shift 等。
            var keyId = VRKeyboardKeyUtility.GetKeyId(key);
            if (string.IsNullOrEmpty(keyId))
                return;

            Debug.Log($"Resolved keyId: {keyId}");

            // 交给文本组合器处理功能键、大小写、符号映射，并写入输出框。
            if (m_SwipeInput != null && m_SwipeInput.TryHandleCandidateSelection(keyId))
                return;

            VRKeyboardTextComposer.HandleKey(
                keyId,
                m_OutputField,
                ref m_CapsLockEnabled,
                ref m_ShiftEnabled,
                m_UseTabCharacter,
                m_TabSpaces);
        }
    }
}
