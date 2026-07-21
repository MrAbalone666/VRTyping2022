using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using VRTyping.Keyboard;

namespace VRTyping.Tests
{
    // 打字测试的核心控制脚本。
    // 它不负责判断 VR 键盘按键怎么被按下，而是监听 TMP_InputField 的文本变化，
    // 再完成目标句显示、玩家输入对比、错误标红、计时、自动下一句和最终结果统计。
    public class TypingTestSession : MonoBehaviour
    {
        [Header("Input")]
        // 玩家最终输入文字的 TMP 输入框。
        // VRKeyboardController / VRKeyboardTextComposer 会把键盘输入写进这个输入框，
        // 本脚本通过 onValueChanged 监听它的内容变化。
        public TMP_InputField m_InputField;

        // 显示当前目标句子的文本组件，通常放在输入框上方。
        // RefreshTargetDisplay 会给已完成字符、当前字符、未输入字符分别上色。
        public TMP_Text m_TargetText;

        // 显示玩家输入内容的对比文本组件。
        // RefreshPlayerDisplay 会把正确字符、错误字符、多余字符分别用不同颜色显示。
        public TMP_Text m_PlayerComparisonText;

        // 倒计时显示文本。
        public TMP_Text m_TimerText;
       

        [Header("Test")]
        // 勾选表示练习模式，取消勾选表示正式测试。
        // 状态会与 ResultOutput.m_IsPractice 保持一致。
        public Toggle m_IsPracticeToggle;

        // 测试句子列表。可以在 Inspector 里放多个句子。
        // 如果开启 m_AutoNextSentences，当前句输入结束后会自动切到下一句。
        public string[] m_Sentences =
        {
            "This is a sentence."
        };

        // 练习模式使用的句子列表，不会与正式测试句子混用。
        public string[] m_PracticeSentences =
        {
            "This is a testing sentence."
        };

        // 单次测试总时长，单位是秒。
        public float m_TestDurationSeconds = 30f;

        // 为 true 时，计时从第一次实际输入开始；为 false 时，Reset 后立即开始计时。
        public bool m_StartOnFirstInput = true;

        // 为 true 时，玩家输入长度达到目标句长度后就认为当前句完成。
        public bool m_FinishWhenTargetLengthReached = true;

        // 为 true 时，完成当前句后会自动进入 m_Sentences 里的下一句。
        public bool m_AutoNextSentences = true;

        // Reset 测试时是否清空输入框。
        public bool m_ClearInputOnStart = true;

        // 是否隐藏 TMP_InputField 自带的原始文字。
        // 如果用单独的彩色对比层显示输入内容，通常需要隐藏原始文字，避免两层文字重叠。
        public bool m_HideNativeInputFieldText;

        // 是否在输入框内部创建一个彩色对比文本层。
        // 这样可以在输入框里直接看到正确/错误字符颜色，而不是只显示在外部文本上。
        public bool m_ShowColoredComparisonInInputField = true;

        [Header("Colors")]
        // 目标句中尚未输入的普通字符颜色。
        public Color m_TargetColor = new Color(0.92f, 0.92f, 0.92f, 1f);

        // TMP_InputField 原生文字颜色。隐藏原生文字时只会把透明度压低，不会直接禁用组件。
        public Color m_InputFieldTextColor = Color.black;

        // TMP_InputField placeholder 颜色。
        public Color m_InputFieldPlaceholderColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);

        // 玩家输入正确字符的颜色。
        public Color m_PlayerCorrectColor = Color.black;

        // 玩家输入错误字符的颜色。
        public Color m_PlayerErrorColor = Color.red;

        // 玩家输入超过目标句长度的多余字符颜色。
        public Color m_PlayerExtraColor = Color.yellow;

        // 目标句中“当前应该输入的下一个字符”的颜色。
        public Color m_CurrentTargetColor = new Color(0.15f, 0.82f, 1f, 1f);

        // 目标句中已经被玩家输入过的位置颜色。
        public Color m_CompletedTargetColor = new Color(0.65f, 0.65f, 0.65f, 1f);

        [Header("Result Panel")]
        // 最终成绩面板。测试结束时会 SetActive(true)，Reset 时会隐藏。
        public RectTransform m_ResultPanel;

        // 成绩面板标题文本，例如 "Your Test Score"。
        public TMP_Text m_ResultTitleText;

        // 成绩面板统计文本，显示 Gross WPM、Accuracy、Typos、Net WPM。
        public TMP_Text m_ResultStatsText;

        // 负责收集试次数据并在测试结束时导出 CSV。
        public ResultOutput m_ResultOutput;

        // 当前正在测试的目标句。
        string m_TargetSentence;

        // 当前句子在 m_Sentences 数组中的索引。
        int m_CurrentSentenceIndex;

        // 已经完成并提交统计的句子的总输入字符数。
        int m_CompletedTypedChars;

        // 已经完成并提交统计的句子的总目标字符数。
        // 用于按 CER = Typos / TargetChars 计算准确率。
        int m_CompletedTargetChars;

        // 已经完成并提交统计的句子的总错误数。
        int m_CompletedTypos;

        // 测试是否已经开始计时。
        bool m_TestStarted;

        // 测试是否已经结束，结束后不再响应输入变化。
        bool m_TestFinished;

        // 当前句子的统计是否已经被提交，防止同一句重复计入总分。
        bool m_CurrentSentenceCommitted;

        // 当前句子的逐句结果是否已经交给 ResultOutput，防止重复导出。
        bool m_CurrentSentenceResultRecorded;

        // 测试开始时的单调实时时间，不受 Time.timeScale 影响。
        double m_StartedAt;

        // 当前剩余秒数。
        float m_RemainingSeconds;

        // 代码主动清空输入框时使用的保护标记。
        // 防止 ClearInputFieldWithoutEvent 修改 text 后又触发 HandleInputChanged。
        bool m_SuppressInputEvent;

        // 用于计算本次文本变化实际插入了多少字符。
        string m_PreviousPlayerText = string.Empty;

        void Awake()
        {
            // 场景加载时先补齐必要引用和运行时 UI，再把测试重置到初始状态。
            CacheReferences();
            SynchronizePracticeToggle();
            EnsureRuntimeUi();
            ResetTest();
        }

        void OnEnable()
        {
            // 监听 TMP_InputField 的文本变化。
            // VR 键盘、物理键盘或代码只要修改了 m_InputField.text，都会进入 HandleInputChanged。
            if (m_InputField != null)
                m_InputField.onValueChanged.AddListener(HandleInputChanged);
            if (m_IsPracticeToggle != null)
                m_IsPracticeToggle.onValueChanged.AddListener(HandlePracticeModeChanged);
            VRKeyboardInputTelemetry.PhysicalActionRecorded += HandlePhysicalInputAction;
        }

        void OnDisable()
        {
            // 组件禁用时移除监听，避免对象重复启用后同一个回调被绑定多次。
            if (m_InputField != null)
                m_InputField.onValueChanged.RemoveListener(HandleInputChanged);
            if (m_IsPracticeToggle != null)
                m_IsPracticeToggle.onValueChanged.RemoveListener(HandlePracticeModeChanged);
            VRKeyboardInputTelemetry.PhysicalActionRecorded -= HandlePhysicalInputAction;
        }

        void Update()
        {
            // 只有测试已经开始并且尚未结束时才更新倒计时。
            if (!m_TestStarted || m_TestFinished)
                return;

            m_RemainingSeconds = Mathf.Max(
                0f,
                m_TestDurationSeconds - (float)(Time.realtimeSinceStartupAsDouble - m_StartedAt));
            RefreshTimer();

            // 倒计时归零后自动结束测试并显示结果。
            if (m_RemainingSeconds <= 0f)
                FinishTest();
        }

        public void ResetTest()
        {
            // 根据练习/正式模式选择句库，再找到第一个非空句子作为试次起点。
            m_CurrentSentenceIndex = FindFirstSentenceIndex();
            m_TargetSentence = GetSentence(m_CurrentSentenceIndex);

            // 清空所有累计统计数据。
            m_CompletedTypedChars = 0;
            m_CompletedTargetChars = 0;
            m_CompletedTypos = 0;
            m_CurrentSentenceCommitted = false;
            m_CurrentSentenceResultRecorded = false;

            // 如果设置为“第一次输入才开始”，Reset 后先保持未开始状态。
            // 否则 Reset 之后立即开始计时。
            m_TestStarted = !m_StartOnFirstInput;
            m_TestFinished = false;
            m_StartedAt = Time.realtimeSinceStartupAsDouble;
            m_RemainingSeconds = m_TestDurationSeconds;

            if (m_ResultOutput != null)
            {
                m_ResultOutput.PrepareTrial();
                if (!m_StartOnFirstInput)
                    m_ResultOutput.BeginTrial();
            }

            // 隐藏上一次测试留下的成绩面板。
            if (m_ResultPanel != null)
                m_ResultPanel.gameObject.SetActive(false);

            // 根据设置决定 Reset 时是否清空输入框。
            if (m_InputField != null && m_ClearInputOnStart)
                ClearInputFieldWithoutEvent();

            m_PreviousPlayerText = GetPlayerText();

            // 让输入框重新获得焦点，方便继续输入。
            if (m_InputField != null)
                m_InputField.ActivateInputField();

            // 刷新所有 UI 到初始状态。
            RefreshTargetDisplay(GetPlayerText());
            RefreshPlayerDisplay(GetPlayerText());
            RefreshTimer();
        }

        public void FinishTest()
        {
            // 防止重复结束导致重复弹出或重复计算。
            if (m_TestFinished)
                return;

            m_TestFinished = true;
            m_RemainingSeconds = Mathf.Max(0f, m_RemainingSeconds);
            RefreshTimer();

            // 超时或手动结束时，当前句可能尚未达到目标长度，也需要保存逐句结果。
            RecordCurrentSentenceResult(GetPlayerText(), false);

            // 计算并显示最终结果。
            ShowResults();
        }

        void CacheReferences()
        {
            // 如果 Inspector 没有手动挂 InputField，就在场景里自动找一个。
            if (m_InputField == null)
                m_InputField = FindObjectOfType<TMP_InputField>(true);

            if (m_ResultOutput == null)
                m_ResultOutput = FindObjectOfType<ResultOutput>(true);

            // 场景没有手动挂 ResultOutput 时自动补上，保证测试结束仍能导出。
            if (m_ResultOutput == null)
                m_ResultOutput = gameObject.AddComponent<ResultOutput>();

            // Inspector 没有手动挂载时，按对象名自动寻找练习模式 Toggle。
            if (m_IsPracticeToggle == null)
            {
                var allToggles = FindObjectsOfType<Toggle>(true);
                for (var i = 0; i < allToggles.Length; i++)
                {
                    if (allToggles[i] != null && allToggles[i].name == "IsPracticeToggle")
                    {
                        m_IsPracticeToggle = allToggles[i];
                        break;
                    }
                }
            }

            // Timer 文本如果没有手动挂载，则按对象名 RemainingTime 查找。
            if (m_TimerText == null)
            {
                var allText = FindObjectsOfType<TMP_Text>(true);
                for (var i = 0; i < allText.Length; i++)
                {
                    if (allText[i] != null && allText[i].name == "RemainingTime")
                    {
                        m_TimerText = allText[i];
                        break;
                    }
                }
            }
        }

        void EnsureRuntimeUi()
        {
            // 优先把运行时创建的 UI 放在当前 Canvas 下。
            // 如果本物体不在 Canvas 里，就在场景中找一个 Canvas；再没有就挂在自己下面。
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                canvas = FindObjectOfType<Canvas>(true);

            var parent = canvas != null ? canvas.transform : transform;
            

            // 如果没有目标句文本，就运行时创建一个默认文本。
            // 正式场景里一般建议在 Inspector 中手动挂好，方便调布局。
            if (m_TargetText == null)
            {
                m_TargetText = CreateText(
                    "Typing Test Target",
                    parent,
                    new Vector2(490f, 86f),
                    new Vector2(16f, 108f),
                    28f,
                    FontStyles.Bold,
                    TextAlignmentOptions.BottomLeft);
            }

            // 如果玩家对比文本没有挂载，或者错误地挂成了 InputField 自带 textComponent，
            // 就根据设置创建一个新的对比文本层。
            var usingNativeInputTextAsComparison = IsInputFieldTextComponent(m_PlayerComparisonText);
            if (m_PlayerComparisonText == null || usingNativeInputTextAsComparison)
            {
                m_PlayerComparisonText = m_ShowColoredComparisonInInputField && m_InputField != null
                    ? CreateInputFieldComparisonText()
                    : CreateText(
                        "Typing Test Player Input",
                        parent,
                        new Vector2(490f, 110f),
                        new Vector2(16f, 8f),
                        25f,
                        FontStyles.Normal,
                        TextAlignmentOptions.TopLeft);
            }

            // 如果没有成绩面板，就创建一个简单的默认成绩面板。
            if (m_ResultPanel == null)
                CreateResultPanel(parent);

            // 统一打开富文本和自动换行，并处理 InputField 原生文字是否隐藏。
            ConfigureText(m_TargetText);
            ConfigureText(m_PlayerComparisonText);
            ConfigureInputFieldTextVisibility();
        }

       

        TMP_Text CreateText(
            string objectName,
            Transform parent,
            Vector2 size,
            Vector2 anchoredPosition,
            float fontSize,
            FontStyles style,
            TextAlignmentOptions alignment)
        {
            // 创建一个带 RectTransform + CanvasRenderer + TextMeshProUGUI 的 UI 文本对象。
            var textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);

            // 使用居中锚点和指定 anchoredPosition，方便作为默认 UI 放在 Canvas 中间附近。
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            // 打开 richText，因为后续会使用 <color=#...> 给每个字符上色。
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            text.richText = true;
            text.raycastTarget = false;
            text.color = Color.white;
            text.margin = new Vector4(12f, 8f, 12f, 8f);
            return text;
        }

        void ConfigureText(TMP_Text text)
        {
            if (text == null)
                return;

            // 确保目标文本和对比文本都支持 TMP 富文本颜色标签。
            text.richText = true;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
        }

        void ConfigureInputFieldTextVisibility()
        {
            if (m_InputField == null)
                return;

            // 如果在输入框内部额外创建了彩色对比层，就把 InputField 自带文字变得近乎透明。
            // 不直接禁用 textComponent，是为了保留 TMP_InputField 的光标、选择和布局行为。
            var hideNativeText = m_HideNativeInputFieldText ||
                (m_ShowColoredComparisonInInputField && !IsInputFieldTextComponent(m_PlayerComparisonText));

            if (m_InputField.textComponent != null)
            {
                var textColor = m_InputFieldTextColor;
                textColor.a = hideNativeText ? 0.02f : Mathf.Max(0.02f, textColor.a);
                m_InputField.textComponent.color = textColor;
            }

            if (m_InputField.placeholder is TMP_Text placeholderText)
            {
                var placeholderColor = m_InputFieldPlaceholderColor;
                placeholderColor.a = hideNativeText ? 0.02f : Mathf.Max(0.02f, placeholderColor.a);
                placeholderText.color = placeholderColor;
            }
        }

        bool IsInputFieldTextComponent(TMP_Text text)
        {
            // 判断传入的文本是否就是 TMP_InputField 自带的文字组件。
            return m_InputField != null &&
                text != null &&
                text == m_InputField.textComponent;
        }

        TMP_Text CreateInputFieldComparisonText()
        {
            // 彩色对比层优先放到 InputField 的 textViewport 下，
            // 这样它的裁剪范围、位置和输入框正文区域保持一致。
            var parent = m_InputField.textViewport != null
                ? m_InputField.textViewport
                : m_InputField.GetComponent<RectTransform>();

            var textObject = new GameObject(
                "Colored Comparison Text",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);

            // 铺满输入框文本区域，保证彩色字符和原本输入框文字对齐。
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            // 尽量复制 InputField 原本文字的字体、字号、对齐、字距等属性。
            // 这样隐藏原生文字后，彩色层看起来仍然像是在输入框里打字。
            var sourceText = m_InputField.textComponent;
            var text = textObject.GetComponent<TextMeshProUGUI>();
            if (sourceText != null)
            {
                text.font = sourceText.font;
                text.fontSharedMaterial = sourceText.fontSharedMaterial;
                text.fontSize = sourceText.fontSize;
                text.fontStyle = sourceText.fontStyle;
                text.alignment = sourceText.alignment;
                text.margin = sourceText.margin;
                text.lineSpacing = sourceText.lineSpacing;
                text.characterSpacing = sourceText.characterSpacing;
                text.wordSpacing = sourceText.wordSpacing;
            }
            else
            {
                text.fontSize = 24f;
                text.alignment = TextAlignmentOptions.TopLeft;
                text.margin = new Vector4(8f, 6f, 8f, 6f);
            }

            // 对比层只负责显示，不接收射线点击，避免挡住输入框本身的交互。
            text.raycastTarget = false;
            text.richText = true;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.color = Color.white;
            return text;
        }

        void CreateResultPanel(Transform parent)
        {
            // 如果场景里没有手动制作结果面板，就动态创建一个默认面板。
            var panelObject = new GameObject("Typing Test Result Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelObject.transform.SetParent(parent, false);

            // 默认面板放在 Canvas 中央附近。
            m_ResultPanel = panelObject.GetComponent<RectTransform>();
            m_ResultPanel.anchorMin = new Vector2(0.5f, 0.5f);
            m_ResultPanel.anchorMax = new Vector2(0.5f, 0.5f);
            m_ResultPanel.pivot = new Vector2(0.5f, 0.5f);
            m_ResultPanel.sizeDelta = new Vector2(470f, 190f);
            m_ResultPanel.anchoredPosition = new Vector2(0f, 18f);

            // 半透明深色背景，让成绩文字在 VR 中更容易看清。
            var image = panelObject.GetComponent<Image>();
            image.color = new Color(0.28f, 0.32f, 0.32f, 0.94f);
            image.raycastTarget = true;

            // 成绩标题。
            m_ResultTitleText = CreateText(
                "Result Title",
                panelObject.transform,
                new Vector2(430f, 48f),
                new Vector2(0f, 58f),
                28f,
                FontStyles.Bold | FontStyles.Italic,
                TextAlignmentOptions.Center);

            // 成绩数字文本。
            m_ResultStatsText = CreateText(
                "Result Stats",
                panelObject.transform,
                new Vector2(430f, 96f),
                new Vector2(0f, -20f),
                22f,
                FontStyles.Bold,
                TextAlignmentOptions.Center);

            // 测试开始时成绩面板默认隐藏，只有 FinishTest 后显示。
            m_ResultPanel.gameObject.SetActive(false);
        }

        void HandleInputChanged(string playerText)
        {
            // 如果是脚本内部清空输入框造成的变化，或者测试已经结束，就不处理。
            if (m_SuppressInputEvent || m_TestFinished)
                return;

            // 第一次输入时才开始计时。
            // 这样玩家准备阶段不会被计入测试时间。
            if (!m_TestStarted && !string.IsNullOrEmpty(playerText))
                StartTiming();

            // CommittedCharacterCount 记录所有实际插入过的字符，包括以后被退格删除的字符。
            if (m_ResultOutput != null)
                m_ResultOutput.RecordTextChange(m_PreviousPlayerText, playerText);
            m_PreviousPlayerText = playerText ?? string.Empty;

            // 每次输入变化都刷新目标句高亮和玩家输入错误标红。
            RefreshTargetDisplay(playerText);
            RefreshPlayerDisplay(playerText);

            // 如果设置为达到目标长度就完成，则在当前输入长度够长时提交当前句。
            if (m_TestStarted &&
                m_FinishWhenTargetLengthReached &&
                !string.IsNullOrEmpty(m_TargetSentence) &&
                playerText.Length >= m_TargetSentence.Length)
            {
                CompleteCurrentSentence(playerText);
            }
        }

        void HandlePhysicalInputAction(VRKeyboardPhysicalActionKind actionKind)
        {
            // 第一次动作即使是空文本退格、Shift 或 CapsLock，也会启动并计入测试。
            if (!m_TestFinished && !m_TestStarted && m_StartOnFirstInput)
                StartTiming();
        }

        void StartTiming()
        {
            if (m_TestStarted)
                return;

            m_TestStarted = true;
            m_StartedAt = Time.realtimeSinceStartupAsDouble;
            m_RemainingSeconds = m_TestDurationSeconds;

            if (m_ResultOutput != null)
                m_ResultOutput.BeginTrial();
        }

        void CompleteCurrentSentence(string playerText)
        {
            // 清空输入框之前保存目标文本、最终输入和本句动作统计。
            RecordCurrentSentenceResult(playerText, true);

            // 先把当前句的 typedChars / targetChars / typos 计入累计统计。
            CommitCurrentSentenceStats(playerText);

            // 如果还有下一句，并且开启自动下一句，就清空输入框并切换目标句。
            if (m_AutoNextSentences && TryFindNextSentenceIndex(m_CurrentSentenceIndex + 1, out var nextIndex))
            {
                m_CurrentSentenceIndex = nextIndex;
                m_TargetSentence = GetSentence(m_CurrentSentenceIndex);
                m_CurrentSentenceCommitted = false;
                m_CurrentSentenceResultRecorded = false;
                ClearInputFieldWithoutEvent();

                if (m_ResultOutput != null)
                    m_ResultOutput.BeginSentence();
                RefreshTargetDisplay(string.Empty);
                RefreshPlayerDisplay(string.Empty);

                if (m_InputField != null)
                    m_InputField.ActivateInputField();

                return;
            }

            // 没有下一句时，结束整个测试。
            FinishTest();
        }

        void RefreshTargetDisplay(string playerText)
        {
            if (m_TargetText == null)
                return;

            var builder = new StringBuilder();
            var playerLength = string.IsNullOrEmpty(playerText) ? 0 : playerText.Length;

            // 逐字符构建目标句显示：
            // 1. 已经输入到的位置使用 completed color；
            // 2. 当前下一位使用 current target color；
            // 3. 后续未输入字符使用 target color。
            for (var i = 0; i < m_TargetSentence.Length; i++)
            {
                var color = i < playerLength
                    ? m_CompletedTargetColor
                    : i == playerLength
                        ? m_CurrentTargetColor
                        : m_TargetColor;
                AppendColoredChar(builder, m_TargetSentence[i], color);
            }

            m_TargetText.text = builder.ToString();
        }

        void RefreshPlayerDisplay(string playerText)
        {
            if (m_PlayerComparisonText == null)
                return;

            // 还没有输入时显示提示文字。
            if (string.IsNullOrEmpty(playerText))
            {
                m_PlayerComparisonText.text = "<color=#8A8A8AFF>Start typing...</color>";
                return;
            }

            var builder = new StringBuilder(playerText.Length * 24);

            // 逐字符比较玩家输入和目标句：
            // 1. 超过目标句长度的字符显示为 Extra；
            // 2. 与目标句对应字符一致则显示为 Correct；
            // 3. 不一致则显示为 Error，也就是错误标红逻辑。
            for (var i = 0; i < playerText.Length; i++)
            {
                var color = i >= m_TargetSentence.Length
                    ? m_PlayerExtraColor
                    : playerText[i] == m_TargetSentence[i]
                        ? m_PlayerCorrectColor
                        : m_PlayerErrorColor;
                AppendColoredChar(builder, playerText[i], color);
            }

            m_PlayerComparisonText.text = builder.ToString();
        }

        void RefreshTimer()
        {
            if (m_TimerText == null)
                return;

            // 使用 CeilToInt，剩余 29.2 秒时显示 30 s，视觉上更符合倒计时习惯。
            m_TimerText.text = Mathf.CeilToInt(m_RemainingSeconds).ToString() + " s";
        }

        void ShowResults()
        {
            // 汇总已完成句子和当前未提交句子的统计数据。
            GetTotalStats(out var typedChars, out var targetChars, out var typos);

            // 实际用时 = 测试总时长 - 剩余时间。
            // 最小限制为 0.01 秒，避免刚开始就结束时出现除以 0。
            var elapsedSeconds = Mathf.Max(0.01f, m_TestDurationSeconds - m_RemainingSeconds);
            var minutes = elapsedSeconds / 60f;

            // Gross WPM：原始输入速度。每 5 个字符算一个 word，空格和标点也计入字符数。
            var grossWpmValue = (typedChars / 5f) / minutes;

            // CER(Character Error Rate)：字符错误率。
            // 这里的 typos 是 Levenshtein Distance，targetChars 是目标文本总字符数。
            var cer = targetChars == 0 ? 0f : typos / (float)targetChars;

            // Accuracy = max(0, 1 - CER)。
            // max(0) 防止错误特别多时准确率变成负数。
            var accuracy01 = Mathf.Max(0f, 1f - cer);

            // 显示用整数结果。
            var grossWpm = Mathf.RoundToInt(grossWpmValue);
            var netWpm = Mathf.RoundToInt(grossWpmValue * accuracy01);
            var accuracy = Mathf.RoundToInt(accuracy01 * 100f);

            if (m_ResultOutput != null)
            {
                m_ResultOutput.CompleteTrial(
                    elapsedSeconds,
                    grossWpmValue,
                    grossWpmValue * accuracy01,
                    typos,
                    cer,
                    accuracy01 * 100f);
            }

            if (m_ResultTitleText != null)
                m_ResultTitleText.text = IsPracticeMode ? "Your Practice Score" : "Your Test Score";

            if (m_ResultStatsText != null)
            {
                // 当前结果面板的文字格式。
                // 如果以后要显示 CER、总字符数或多行表格，可以主要改这里。
                m_ResultStatsText.text =
                    grossWpm + " WPM    x    " +
                    accuracy + "%\n" +
                    "Typing Speed      Accuracy\n\n" +
                    typos + " typos      Net Speed: " + netWpm + " WPM";
            }

            // 最后显示成绩面板。
            if (m_ResultPanel != null)
                m_ResultPanel.gameObject.SetActive(true);
        }

        int FindFirstSentenceIndex()
        {
            // 找到第一个非空句子，避免数组前面有空字符串时测试显示为空。
            var sentences = GetActiveSentences();
            if (sentences == null)
                return 0;

            for (var i = 0; i < sentences.Length; i++)
            {
                if (!string.IsNullOrEmpty(sentences[i]))
                    return i;
            }

            return 0;
        }

        bool TryFindNextSentenceIndex(int startIndex, out int sentenceIndex)
        {
            // 从指定位置开始查找下一个非空句子。
            sentenceIndex = -1;
            var sentences = GetActiveSentences();
            if (sentences == null)
                return false;

            for (var i = Mathf.Max(0, startIndex); i < sentences.Length; i++)
            {
                if (!string.IsNullOrEmpty(sentences[i]))
                {
                    sentenceIndex = i;
                    return true;
                }
            }

            return false;
        }

        string GetSentence(int sentenceIndex)
        {
            // 安全获取当前模式的句子；句库为空时使用对应模式的默认句子。
            var sentences = GetActiveSentences();
            var fallbackSentence = IsPracticeMode
                ? "This is a testing sentence."
                : "This is a sentence.";

            if (sentences == null || sentences.Length == 0)
                return fallbackSentence;

            if (sentenceIndex >= 0 &&
                sentenceIndex < sentences.Length &&
                !string.IsNullOrEmpty(sentences[sentenceIndex]))
            {
                return sentences[sentenceIndex];
            }

            var firstSentenceIndex = FindFirstSentenceIndex();
            return firstSentenceIndex >= 0 &&
                   firstSentenceIndex < sentences.Length &&
                   !string.IsNullOrEmpty(sentences[firstSentenceIndex])
                ? sentences[firstSentenceIndex]
                : fallbackSentence;
        }

        // ResultOutput 是练习/正式状态的数据来源。
        bool IsPracticeMode => m_ResultOutput != null && m_ResultOutput.m_IsPractice;

        // 当前是练习模式就返回练习句库，否则返回正式测试句库。
        string[] GetActiveSentences()
        {
            return IsPracticeMode ? m_PracticeSentences : m_Sentences;
        }

        // 场景启动时让 Toggle 显示 ResultOutput 当前保存的状态，不触发切换事件。
        void SynchronizePracticeToggle()
        {
            if (m_IsPracticeToggle != null)
                m_IsPracticeToggle.SetIsOnWithoutNotify(IsPracticeMode);
        }

        // Toggle 改变时同步 ResultOutput，并使用对应句库重新开始试次。
        void HandlePracticeModeChanged(bool isPractice)
        {
            SetPracticeMode(isPractice);
        }

        // 可供 Toggle、Button 或其他脚本调用的统一模式切换入口。
        public void SetPracticeMode(bool isPractice)
        {
            if (m_ResultOutput == null)
                CacheReferences();

            if (m_ResultOutput != null)
                m_ResultOutput.m_IsPractice = isPractice;

            if (m_IsPracticeToggle != null && m_IsPracticeToggle.isOn != isPractice)
                m_IsPracticeToggle.SetIsOnWithoutNotify(isPractice);

            ResetTest();
        }

        string GetPlayerText()
        {
            // 统一读取玩家当前输入。InputField 不存在时返回空字符串，避免空引用。
            return m_InputField != null ? m_InputField.text : string.Empty;
        }

        void ClearInputFieldWithoutEvent()
        {
            if (m_InputField == null)
                return;

            // 清空输入框时临时屏蔽 onValueChanged 处理。
            // 否则切换下一句时清空输入框会被当作一次玩家输入变化处理。
            m_SuppressInputEvent = true;
            m_InputField.text = string.Empty;
            m_InputField.caretPosition = 0;
            m_InputField.selectionAnchorPosition = 0;
            m_InputField.selectionFocusPosition = 0;
            m_InputField.ForceLabelUpdate();
            m_SuppressInputEvent = false;
            m_PreviousPlayerText = string.Empty;
        }

        void RecordCurrentSentenceResult(string playerText, bool completed)
        {
            if (m_CurrentSentenceResultRecorded || m_ResultOutput == null)
                return;

            playerText = playerText ?? string.Empty;

            // 超时时如果当前句一个字符都没有输入，就不把它写入逐句结果。
            if (!completed && playerText.Length == 0)
            {
                m_CurrentSentenceResultRecorded = true;
                return;
            }

            GetSentenceStats(
                playerText,
                m_TargetSentence,
                !completed,
                out var typedChars,
                out var targetChars,
                out var typos);

            m_ResultOutput.RecordSentence(
                m_CurrentSentenceIndex,
                m_TargetSentence,
                playerText,
                targetChars,
                typedChars,
                typos,
                completed);
            m_CurrentSentenceResultRecorded = true;
        }

        void CommitCurrentSentenceStats(string playerText)
        {
            // 防止当前句被多次提交。例如输入达到长度和时间结束几乎同时发生时。
            if (m_CurrentSentenceCommitted)
                return;

            // 当前句完成后，把本句统计加入累计统计。
            GetSentenceStats(
                playerText,
                m_TargetSentence,
                false,
                out var typedChars,
                out var targetChars,
                out var typos);
            m_CompletedTypedChars += typedChars;
            m_CompletedTargetChars += targetChars;
            m_CompletedTypos += typos;
            m_CurrentSentenceCommitted = true;
        }

        void GetTotalStats(out int typedChars, out int targetChars, out int typos)
        {
            // 先取已经完成并提交过的句子统计。
            typedChars = m_CompletedTypedChars;
            targetChars = m_CompletedTargetChars;
            typos = m_CompletedTypos;

            // 如果当前句已经提交过，就不能再重复把当前输入计入。
            if (m_CurrentSentenceCommitted)
                return;

            var currentPlayerText = GetPlayerText();

            // 当前句完全没有开始输入时，不把它计入总字符数、错误数和准确率。
            if (string.IsNullOrEmpty(currentPlayerText))
                return;

            // 对超时未完成的当前句，只比较玩家已经尝试输入的目标前缀。
            // 计时结束后尚未来得及输入的目标后缀不会被当作删除错误。
            GetSentenceStats(
                currentPlayerText,
                m_TargetSentence,
                true,
                out var currentTypedChars,
                out var currentTargetChars,
                out var currentTypos);
            typedChars += currentTypedChars;
            targetChars += currentTargetChars;
            typos += currentTypos;
        }

        void GetSentenceStats(
            string playerText,
            string targetSentence,
            bool compareAttemptedPrefixOnly,
            out int typedChars,
            out int targetChars,
            out int typos)
        {
            // 统一把 null 转为空字符串，保证下面的长度和距离计算不会报错。
            playerText = playerText ?? string.Empty;
            targetSentence = targetSentence ?? string.Empty;

            // 超时未完成时，只保留与输入长度对应的目标前缀。
            // 如果玩家输入超过目标长度，仍保留完整目标，让多余字符计为插入错误。
            var comparisonTarget = targetSentence;
            if (compareAttemptedPrefixOnly && playerText.Length < targetSentence.Length)
                comparisonTarget = targetSentence.Substring(0, playerText.Length);

            // typedChars 是玩家实际输入字符数，包含空格和标点。
            typedChars = playerText.Length;

            // 未完成句使用已尝试目标前缀的长度，完成句使用完整目标长度。
            targetChars = comparisonTarget.Length;

            // typos 使用编辑距离：替换、插入、删除各算 1 次错误。
            typos = ComputeLevenshteinDistance(playerText, comparisonTarget);
        }

        void AppendColoredChar(StringBuilder builder, char value, Color color)
        {
            // 使用 TMP rich text 的 <color=#RRGGBBAA> 标签给单个字符上色。
            builder.Append("<color=#");
            builder.Append(ColorUtility.ToHtmlStringRGBA(color));
            builder.Append(">");
            AppendEscapedChar(builder, value);
            builder.Append("</color>");
        }

        void AppendEscapedChar(StringBuilder builder, char value)
        {
            // 对 TMP 富文本里的特殊字符做转义。
            // 否则玩家输入 '<'、'>'、'&' 时可能会被 TextMeshPro 当作标签解析。
            switch (value)
            {
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '&':
                    builder.Append("&amp;");
                    break;
                case '\n':
                    builder.Append('\n');
                    break;
                case '\t':
                    builder.Append("    ");
                    break;
                default:
                    builder.Append(value);
                    break;
            }
        }

        int ComputeLevenshteinDistance(string source, string target)
        {
            // 计算 Levenshtein Distance，也就是把 source 变成 target 所需的最少编辑次数。
            // 三种操作的代价都是 1：插入一个字符、删除一个字符、替换一个字符。
            source = source ?? string.Empty;
            target = target ?? string.Empty;

            // 只保留上一行和当前行，空间复杂度从 O(source * target) 降到 O(target)。
            var previous = new int[target.Length + 1];
            var current = new int[target.Length + 1];

            // source 为空时，变成 target[0..j] 需要插入 j 个字符。
            for (var j = 0; j <= target.Length; j++)
                previous[j] = j;

            for (var i = 1; i <= source.Length; i++)
            {
                // target 为空时，source[0..i] 变成空字符串需要删除 i 个字符。
                current[0] = i;
                for (var j = 1; j <= target.Length; j++)
                {
                    // 当前两个字符相同则替换代价为 0，否则替换代价为 1。
                    var cost = source[i - 1] == target[j - 1] ? 0 : 1;

                    // 三种可能：
                    // 1. current[j - 1] + 1：插入 target[j - 1]；
                    // 2. previous[j] + 1：删除 source[i - 1]；
                    // 3. previous[j - 1] + cost：匹配或替换当前字符。
                    current[j] = Math.Min(
                        Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + cost);
                }

                // 当前行计算完后，交换数组引用，下一轮继续复用内存。
                var swap = previous;
                previous = current;
                current = swap;
            }

            // previous[target.Length] 就是完整 source 到完整 target 的编辑距离。
            return previous[target.Length];
        }
    }
}
