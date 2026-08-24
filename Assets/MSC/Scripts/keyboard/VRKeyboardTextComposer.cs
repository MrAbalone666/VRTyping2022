using System;
using System.Collections.Generic;
using System.Text;
using TMPro;

namespace VRTyping.Keyboard
{
    public enum VRKeyboardPhysicalActionKind
    {
        Key,
        Swipe,
        Backspace,
        Shift,
        CapsLock,
        CandidateSelection,
    }

    // 统一发布键盘层的物理输入动作，供实验记录器统计。
    public static class VRKeyboardInputTelemetry
    {
        public static event Action InputStarted;
        public static event Action<VRKeyboardPhysicalActionKind> PhysicalActionRecorded;

        public static void NotifyInputStarted()
        {
            InputStarted?.Invoke();
        }

        public static void RecordKeyAction(string keyId)
        {
            var kind = keyId == "Back"
                ? VRKeyboardPhysicalActionKind.Backspace
                : keyId == "Cap"
                    ? VRKeyboardPhysicalActionKind.CapsLock
                    : keyId == "Shift" || keyId == "Shift_1"
                        ? VRKeyboardPhysicalActionKind.Shift
                        : VRKeyboardPhysicalActionKind.Key;

            PhysicalActionRecorded?.Invoke(kind);
        }

        public static void RecordSwipeAction()
        {
            PhysicalActionRecorded?.Invoke(VRKeyboardPhysicalActionKind.Swipe);
        }

        public static void RecordCandidateSelectionAction()
        {
            PhysicalActionRecorded?.Invoke(VRKeyboardPhysicalActionKind.CandidateSelection);
        }
    }

    // VR 键盘文本组合工具：把按键 ID 转成实际输入内容，并写入 TMP_InputField。
    public static class VRKeyboardTextComposer
    {
        // 不按 Shift 时，特殊按键 ID 对应的符号。
        static readonly Dictionary<string, string> s_UnshiftedSymbols = new Dictionary<string, string>
        {
            { "minus", "-" },
            { "Plus", "=" },
            { "LKuohao", "[" },
            { "RKuohao", "]" },
            { "LMaohao", ";" },
            { "RMaohao", "'" },
            { "less", "," },
            { "More", "." },
            { "ques", "/" },
            { "Tab_1", "\\" },
        };

        // 按住 Shift 时，特殊按键 ID 对应的符号。
        static readonly Dictionary<string, string> s_ShiftedSymbols = new Dictionary<string, string>
        {
            { "minus", "_" },
            { "Plus", "+" },
            { "LKuohao", "{" },
            { "RKuohao", "}" },
            { "LMaohao", ":" },
            { "RMaohao", "\"" },
            { "less", "<" },
            { "More", ">" },
            { "ques", "?" },
            { "Tab_1", "|" },
        };

        // 数字键在 Shift 状态下对应的符号
        static readonly Dictionary<string, string> s_ShiftedDigits = new Dictionary<string, string>
        {
            { "1", "!" },
            { "2", "@" },
            { "3", "#" },
            { "4", "$" },
            { "5", "%" },
            { "6", "^" },
            { "7", "&" },
            { "8", "*" },
            { "9", "(" },
            { "0", ")" },
        };

        public static string GetText(TMP_InputField outputField)
        {
            // 输入框为空时返回空字符串
            return outputField != null ? outputField.text : string.Empty;
        }

        public static void ClearText(TMP_InputField outputField)
        {
            // 清空输入框内容，并复用 SetOutput 统一处理光标和刷新
            SetOutput(outputField, string.Empty);
        }

        public static void AppendText(TMP_InputField outputField, string text)
        {
            // 在当前输入框文本末尾追加内容
            SetOutput(outputField, GetText(outputField) + text);
        }

        public static void Backspace(TMP_InputField outputField)
        {
            // 删除最后一个字符；没有内容时不做任何处理
            var text = GetText(outputField);
            if (string.IsNullOrEmpty(text))
                return;

            SetOutput(outputField, text.Substring(0, text.Length - 1));
        }

        public static void SetOutput(TMP_InputField outputField, string text)
        {
            if (outputField == null)
                return;

            // 更新文本后把光标和选区都移动到末尾
            outputField.text = text;
            outputField.caretPosition = text.Length;
            outputField.selectionAnchorPosition = text.Length;
            outputField.selectionFocusPosition = text.Length;
            outputField.ForceLabelUpdate();
            outputField.ActivateInputField();
        }

        public static bool HandleKey(
            string keyId,
            TMP_InputField outputField,
            ref bool capsLockEnabled,
            ref bool shiftEnabled,
            bool useTabCharacter,
            int tabSpaces)
        {
            // 处理一个键盘按键。返回 true 表示这个 keyId 被识别并消费掉
            if (string.IsNullOrEmpty(keyId))
                return false;

            switch (keyId)
            {
                case "Back":
                    Backspace(outputField);
                    return true;
                case "Enter":
                    AppendText(outputField, "\n");
                    return true;
                case "Space":
                    AppendText(outputField, " ");
                    return true;
                case "Tab":
                    // Tab 可以输入真实制表符，也可以按配置转换成若干空格
                    AppendText(outputField, useTabCharacter ? "\t" : new string(' ', tabSpaces));
                    return true;
                case "Cap":
                    // CapsLock 是持续状态，每次按下都会切换开/关。
                    capsLockEnabled = !capsLockEnabled;
                    return true;
                case "Shift":
                case "Shift_1":
                    // Shift 是临时状态；输入一个可打印字符后会在下面自动关闭。
                    shiftEnabled = !shiftEnabled;
                    return true;
                case "ESC":
                case "Fn":
                case "LAlt":
                case "RAlt":
                case "LCtrL":
                case "RCTRL":
                case "LWin":
                case "RWin":
                    return true;
            }

            // 不是功能键时，尝试把 keyId 解析成可输入字符。
            if (!TryResolvePrintableKey(keyId, capsLockEnabled, shiftEnabled, out var textToAppend))
                return false;

            AppendText(outputField, textToAppend);

            // 这里把 Shift 当作一次性 Shift：输入一个字符后自动释放。
            if (shiftEnabled)
                shiftEnabled = false;

            return true;
        }

        public static bool TryResolvePrintableKey(
            string keyId,
            bool capsLockEnabled,
            bool shiftEnabled,
            out string value)
        {
            value = string.Empty;
            if (string.IsNullOrEmpty(keyId))
                return false;

            // 单个英文字母根据 CapsLock 和 Shift 决定大小写。
            if (keyId.Length == 1 && char.IsLetter(keyId[0]))
            {
                value = ApplyLetterCase(keyId, capsLockEnabled, shiftEnabled);
                return true;
            }

            // 数字键在 Shift 状态下变成上排符号，否则保持数字本身。
            if (keyId.Length == 1 && char.IsDigit(keyId[0]))
            {
                if (shiftEnabled && s_ShiftedDigits.TryGetValue(keyId, out value))
                    return true;

                value = keyId;
                return true;
            }

            // 先查 Shift 符号表，再查普通符号表。
            if (shiftEnabled && s_ShiftedSymbols.TryGetValue(keyId, out value))
                return true;

            if (s_UnshiftedSymbols.TryGetValue(keyId, out value))
                return true;

            return false;
        }

        public static string ApplyLetterCase(string value, bool capsLockEnabled, bool shiftEnabled)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            // CapsLock 和 Shift 使用异或：只有一个开启时为大写，两个同时开启时回到小写。
            var uppercase = capsLockEnabled ^ shiftEnabled;
            return uppercase ? value.ToUpperInvariant() : value.ToLowerInvariant();
        }

        public static string ApplySwipeWordCase(string value, bool capsLockEnabled, bool shiftEnabled)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            // Swipe commits a whole word at once, so its Shift behavior differs from
            // single-key input: CapsLock uppercases the complete word, while Shift
            // capitalizes only the first letter and is consumed after that word.
            if (capsLockEnabled)
                return value.ToUpperInvariant();

            var normalized = value.ToLowerInvariant();
            if (!shiftEnabled)
                return normalized;

            var characters = normalized.ToCharArray();
            for (var i = 0; i < characters.Length; i++)
            {
                if (!char.IsLetter(characters[i]))
                    continue;

                characters[i] = char.ToUpperInvariant(characters[i]);
                break;
            }

            return new string(characters);
        }

        public static string BuildLetterSequence(
            IList<string> keyIds,
            bool capsLockEnabled,
            bool shiftEnabled)
        {
            if (keyIds == null)
                return string.Empty;

            // 把一串按键 ID 组合成字母序列，常用于滑动输入/轨迹识别前的候选字符串。
            var builder = new StringBuilder(keyIds.Count);

            for (var i = 0; i < keyIds.Count; i++)
            {
                var keyId = keyIds[i];
                if (string.IsNullOrEmpty(keyId) || keyId.Length != 1 || !char.IsLetter(keyId[0]))
                    continue;

                builder.Append(char.ToLowerInvariant(keyId[0]));
            }

            return ApplySwipeWordCase(builder.ToString(), capsLockEnabled, shiftEnabled);
        }
    }
}
