using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace VRTyping.Tests
{
    public class TypingTestSession : MonoBehaviour
    {
        [Header("Input")]
        public TMP_InputField m_InputField;
        public TMP_Text m_TargetText;
        public TMP_Text m_PlayerComparisonText;
        public TMP_Text m_TimerText;
       

        [Header("Test")]
        public string[] m_Sentences =
        {
            "This is a testing sentence."
        };

        public float m_TestDurationSeconds = 30f;
        public bool m_StartOnFirstInput = true;
        public bool m_FinishWhenTargetLengthReached = true;
        public bool m_AutoNextSentences = true;
        public bool m_ClearInputOnStart = true;
        public bool m_HideNativeInputFieldText;
        public bool m_ShowColoredComparisonInInputField = true;

        [Header("Colors")]
        public Color m_TargetColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        public Color m_InputFieldTextColor = Color.black;
        public Color m_InputFieldPlaceholderColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
        public Color m_PlayerCorrectColor = Color.black;
        public Color m_PlayerErrorColor = Color.red;
        public Color m_PlayerExtraColor = Color.yellow;
        public Color m_CurrentTargetColor = new Color(0.15f, 0.82f, 1f, 1f);
        public Color m_CompletedTargetColor = new Color(0.65f, 0.65f, 0.65f, 1f);

        [Header("Result Panel")]
        public RectTransform m_ResultPanel;
        public TMP_Text m_ResultTitleText;
        public TMP_Text m_ResultStatsText;

        string m_TargetSentence;
        int m_CurrentSentenceIndex;
        int m_CompletedTypedChars;
        int m_CompletedTargetChars;
        int m_CompletedTypos;
        bool m_TestStarted;
        bool m_TestFinished;
        bool m_CurrentSentenceCommitted;
        float m_StartedAt;
        float m_RemainingSeconds;
        bool m_SuppressInputEvent;

        void Awake()
        {
            CacheReferences();
            EnsureRuntimeUi();
            ResetTest();
        }

        void OnEnable()
        {
            if (m_InputField != null)
                m_InputField.onValueChanged.AddListener(HandleInputChanged);
        }

        void OnDisable()
        {
            if (m_InputField != null)
                m_InputField.onValueChanged.RemoveListener(HandleInputChanged);
        }

        void Update()
        {
            if (!m_TestStarted || m_TestFinished)
                return;

            m_RemainingSeconds = Mathf.Max(0f, m_TestDurationSeconds - (Time.time - m_StartedAt));
            RefreshTimer();

            if (m_RemainingSeconds <= 0f)
                FinishTest();
        }

        public void ResetTest()
        {
            m_CurrentSentenceIndex = FindFirstSentenceIndex();
            m_TargetSentence = GetSentence(m_CurrentSentenceIndex);
            m_CompletedTypedChars = 0;
            m_CompletedTargetChars = 0;
            m_CompletedTypos = 0;
            m_CurrentSentenceCommitted = false;
            m_TestStarted = !m_StartOnFirstInput;
            m_TestFinished = false;
            m_StartedAt = Time.time;
            m_RemainingSeconds = m_TestDurationSeconds;

            if (m_ResultPanel != null)
                m_ResultPanel.gameObject.SetActive(false);

            if (m_InputField != null && m_ClearInputOnStart)
                ClearInputFieldWithoutEvent();

            if (m_InputField != null)
                m_InputField.ActivateInputField();

            RefreshTargetDisplay(GetPlayerText());
            RefreshPlayerDisplay(GetPlayerText());
            RefreshTimer();
        }

        public void FinishTest()
        {
            if (m_TestFinished)
                return;

            m_TestFinished = true;
            m_RemainingSeconds = Mathf.Max(0f, m_RemainingSeconds);
            RefreshTimer();
            ShowResults();
        }

        void CacheReferences()
        {
            if (m_InputField == null)
                m_InputField = FindObjectOfType<TMP_InputField>(true);

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
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                canvas = FindObjectOfType<Canvas>(true);

            var parent = canvas != null ? canvas.transform : transform;
            

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

            if (m_ResultPanel == null)
                CreateResultPanel(parent);

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
            var textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);

            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

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

            text.richText = true;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
        }

        void ConfigureInputFieldTextVisibility()
        {
            if (m_InputField == null)
                return;

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
            return m_InputField != null &&
                text != null &&
                text == m_InputField.textComponent;
        }

        TMP_Text CreateInputFieldComparisonText()
        {
            var parent = m_InputField.textViewport != null
                ? m_InputField.textViewport
                : m_InputField.GetComponent<RectTransform>();

            var textObject = new GameObject(
                "Colored Comparison Text",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);

            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

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

            text.raycastTarget = false;
            text.richText = true;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.color = Color.white;
            return text;
        }

        void CreateResultPanel(Transform parent)
        {
            var panelObject = new GameObject("Typing Test Result Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelObject.transform.SetParent(parent, false);

            m_ResultPanel = panelObject.GetComponent<RectTransform>();
            m_ResultPanel.anchorMin = new Vector2(0.5f, 0.5f);
            m_ResultPanel.anchorMax = new Vector2(0.5f, 0.5f);
            m_ResultPanel.pivot = new Vector2(0.5f, 0.5f);
            m_ResultPanel.sizeDelta = new Vector2(470f, 190f);
            m_ResultPanel.anchoredPosition = new Vector2(0f, 18f);

            var image = panelObject.GetComponent<Image>();
            image.color = new Color(0.28f, 0.32f, 0.32f, 0.94f);
            image.raycastTarget = true;

            m_ResultTitleText = CreateText(
                "Result Title",
                panelObject.transform,
                new Vector2(430f, 48f),
                new Vector2(0f, 58f),
                28f,
                FontStyles.Bold | FontStyles.Italic,
                TextAlignmentOptions.Center);

            m_ResultStatsText = CreateText(
                "Result Stats",
                panelObject.transform,
                new Vector2(430f, 96f),
                new Vector2(0f, -20f),
                22f,
                FontStyles.Bold,
                TextAlignmentOptions.Center);

            m_ResultPanel.gameObject.SetActive(false);
        }

        void HandleInputChanged(string playerText)
        {
            if (m_SuppressInputEvent || m_TestFinished)
                return;

            if (!m_TestStarted && !string.IsNullOrEmpty(playerText))
            {
                m_TestStarted = true;
                m_StartedAt = Time.time;
                m_RemainingSeconds = m_TestDurationSeconds;
            }

            RefreshTargetDisplay(playerText);
            RefreshPlayerDisplay(playerText);

            if (m_TestStarted &&
                m_FinishWhenTargetLengthReached &&
                !string.IsNullOrEmpty(m_TargetSentence) &&
                playerText.Length >= m_TargetSentence.Length)
            {
                CompleteCurrentSentence(playerText);
            }
        }

        void CompleteCurrentSentence(string playerText)
        {
            CommitCurrentSentenceStats(playerText);

            if (m_AutoNextSentences && TryFindNextSentenceIndex(m_CurrentSentenceIndex + 1, out var nextIndex))
            {
                m_CurrentSentenceIndex = nextIndex;
                m_TargetSentence = GetSentence(m_CurrentSentenceIndex);
                m_CurrentSentenceCommitted = false;
                ClearInputFieldWithoutEvent();
                RefreshTargetDisplay(string.Empty);
                RefreshPlayerDisplay(string.Empty);

                if (m_InputField != null)
                    m_InputField.ActivateInputField();

                return;
            }

            FinishTest();
        }

        void RefreshTargetDisplay(string playerText)
        {
            if (m_TargetText == null)
                return;

            var builder = new StringBuilder();
            var playerLength = string.IsNullOrEmpty(playerText) ? 0 : playerText.Length;

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

            if (string.IsNullOrEmpty(playerText))
            {
                m_PlayerComparisonText.text = "<color=#8A8A8AFF>Start typing...</color>";
                return;
            }

            var builder = new StringBuilder(playerText.Length * 24);
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

            m_TimerText.text = Mathf.CeilToInt(m_RemainingSeconds).ToString() + " s";
        }

        void ShowResults()
        {
            GetTotalStats(out var typedChars, out var targetChars, out var typos);
            var elapsedSeconds = Mathf.Max(0.01f, m_TestDurationSeconds - m_RemainingSeconds);
            var minutes = elapsedSeconds / 60f;
            var grossWpmValue = (typedChars / 5f) / minutes;
            var cer = targetChars == 0 ? 0f : typos / (float)targetChars;
            var accuracy01 = Mathf.Max(0f, 1f - cer);
            var grossWpm = Mathf.RoundToInt(grossWpmValue);
            var netWpm = Mathf.RoundToInt(grossWpmValue * accuracy01);
            var accuracy = Mathf.RoundToInt(accuracy01 * 100f);

            if (m_ResultTitleText != null)
                m_ResultTitleText.text = "Your Test Score";

            if (m_ResultStatsText != null)
            {
                m_ResultStatsText.text =
                    grossWpm + " WPM    x    " +
                    accuracy + "%\n" +
                    "Typing Speed      Accuracy\n\n" +
                    typos + " typos      Net Speed: " + netWpm + " WPM";
            }

            if (m_ResultPanel != null)
                m_ResultPanel.gameObject.SetActive(true);
        }

        int FindFirstSentenceIndex()
        {
            if (m_Sentences == null)
                return 0;

            for (var i = 0; i < m_Sentences.Length; i++)
            {
                if (!string.IsNullOrEmpty(m_Sentences[i]))
                    return i;
            }

            return 0;
        }

        bool TryFindNextSentenceIndex(int startIndex, out int sentenceIndex)
        {
            sentenceIndex = -1;
            if (m_Sentences == null)
                return false;

            for (var i = Mathf.Max(0, startIndex); i < m_Sentences.Length; i++)
            {
                if (!string.IsNullOrEmpty(m_Sentences[i]))
                {
                    sentenceIndex = i;
                    return true;
                }
            }

            return false;
        }

        string GetSentence(int sentenceIndex)
        {
            if (m_Sentences == null || m_Sentences.Length == 0)
                return "This is a testing sentence.";

            if (sentenceIndex >= 0 &&
                sentenceIndex < m_Sentences.Length &&
                !string.IsNullOrEmpty(m_Sentences[sentenceIndex]))
            {
                return m_Sentences[sentenceIndex];
            }

            var firstSentenceIndex = FindFirstSentenceIndex();
            return !string.IsNullOrEmpty(m_Sentences[firstSentenceIndex])
                ? m_Sentences[firstSentenceIndex]
                : "This is a testing sentence.";
        }

        string GetPlayerText()
        {
            return m_InputField != null ? m_InputField.text : string.Empty;
        }

        void ClearInputFieldWithoutEvent()
        {
            if (m_InputField == null)
                return;

            m_SuppressInputEvent = true;
            m_InputField.text = string.Empty;
            m_InputField.caretPosition = 0;
            m_InputField.selectionAnchorPosition = 0;
            m_InputField.selectionFocusPosition = 0;
            m_InputField.ForceLabelUpdate();
            m_SuppressInputEvent = false;
        }

        void CommitCurrentSentenceStats(string playerText)
        {
            if (m_CurrentSentenceCommitted)
                return;

            GetSentenceStats(playerText, m_TargetSentence, out var typedChars, out var targetChars, out var typos);
            m_CompletedTypedChars += typedChars;
            m_CompletedTargetChars += targetChars;
            m_CompletedTypos += typos;
            m_CurrentSentenceCommitted = true;
        }

        void GetTotalStats(out int typedChars, out int targetChars, out int typos)
        {
            typedChars = m_CompletedTypedChars;
            targetChars = m_CompletedTargetChars;
            typos = m_CompletedTypos;

            if (m_CurrentSentenceCommitted)
                return;

            GetSentenceStats(GetPlayerText(), m_TargetSentence, out var currentTypedChars, out var currentTargetChars, out var currentTypos);
            typedChars += currentTypedChars;
            targetChars += currentTargetChars;
            typos += currentTypos;
        }

        void GetSentenceStats(string playerText, string targetSentence, out int typedChars, out int targetChars, out int typos)
        {
            playerText = playerText ?? string.Empty;
            targetSentence = targetSentence ?? string.Empty;

            typedChars = playerText.Length;
            targetChars = targetSentence.Length;
            typos = ComputeLevenshteinDistance(playerText, targetSentence);
        }

        void AppendColoredChar(StringBuilder builder, char value, Color color)
        {
            builder.Append("<color=#");
            builder.Append(ColorUtility.ToHtmlStringRGBA(color));
            builder.Append(">");
            AppendEscapedChar(builder, value);
            builder.Append("</color>");
        }

        void AppendEscapedChar(StringBuilder builder, char value)
        {
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
            source = source ?? string.Empty;
            target = target ?? string.Empty;

            var previous = new int[target.Length + 1];
            var current = new int[target.Length + 1];

            for (var j = 0; j <= target.Length; j++)
                previous[j] = j;

            for (var i = 1; i <= source.Length; i++)
            {
                current[0] = i;
                for (var j = 1; j <= target.Length; j++)
                {
                    var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    current[j] = Math.Min(
                        Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + cost);
                }

                var swap = previous;
                previous = current;
                current = swap;
            }

            return previous[target.Length];
        }
    }
}
