using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using VRTyping.Keyboard;

namespace VRTyping.Tests
{
    // 收集一次打字试次的数据，并在结束时自动追加到 CSV。
    public class ResultOutput : MonoBehaviour
    {
        // CSV 数据结构版本。以后增删列时可以提高版本号，方便区分不同格式的数据。
        const int SchemaVersion = 2;

        // 一次测试的汇总数据文件名。
        const string TrialFileName = "trial_results.csv";

        // 每个句子的明细数据文件名。
        const string SentenceFileName = "sentence_results.csv";

        // 统一使用固定区域格式输出数字，避免部分电脑把小数点写成逗号。
        static readonly CultureInfo s_InvariantCulture = CultureInfo.InvariantCulture;

        // 新建 CSV 时写入 UTF-8 BOM，让 Excel 能正确识别中文。
        static readonly Encoding s_Utf8WithBom = new UTF8Encoding(true);

        // 使用程序启动时的本机时间作为易读的 SessionId，例如 20260717181430。
        static readonly string s_SessionId = DateTime.Now.ToString("yyyyMMddHHmmss", s_InvariantCulture);

        // 当前程序运行中的下一个试次编号，从 T001 开始递增。
        static int s_NextTrialNumber = 1;
       

        // 参与者编号，例如 P001。
        public string m_ParticipantId = "P001";

        // ParticipantTXT/ID 下用于显示和编辑参与者编号的输入框。
        public TMP_InputField m_ParticipantIdInputField;

        // 当前试次是否为练习试次。
        public bool m_IsPractice;

        // 当前试次的数据是否有效。
        public bool m_TrialValid = true;

        // 试次无效时记录具体原因；有效试次可以留空。
        public string m_InvalidReason = string.Empty;


        // 输入模式选择器，用于自动读取本次测试实际使用的输入方式。
        public VRKeyboardInputModeSelector m_InputModeSelector;

        // 找不到输入模式选择器时使用的备用输入方式。
        public VRKeyboardInputMode m_FallbackInputMethod = VRKeyboardInputMode.Press;


        // 测试完成后是否自动导出 CSV。
        public bool m_AutoExportCsv = true;

        // Application.persistentDataPath 下的结果文件夹名称。
        public string m_ExportFolderName = "VRTypingResults";

        // 导出成功后是否在 Unity Console 输出保存路径。
        public bool m_LogExportPath = true;

        // 单个句子的结果数据；对应 sentence_results.csv 中的一行。
        public class SentenceResult
        {
            // 句子在当前试次中的索引。
            public int SentenceIndex;

            // 要求参与者输入的目标文本。
            public string TargetText;

            // 该句结束时输入框中的最终文本。
            public string FinalInputText;

            // 完成或结束该句所用的秒数。
            public double DurationSeconds;

            // 目标文本包含的字符数。
            public int TargetCharacterCount;

            // 最终输入文本包含的字符数。
            public int FinalCharacterCount;

            // 最终输入文本与目标文本之间的编辑距离。
            public int LevenshteinDistance;

            // 字符错误率：编辑距离 / 目标字符数。
            public double CER;

            // 准确率百分比：max(0, 1 - CER) * 100。
            public double AccuracyPercent;

            // 本句发生的物理输入动作总数；一次按键或一次 Swipe 算一次动作。
            public int PhysicalActionCount;

            // 本句曾经写入过的字符总数，被退格删除的字符仍然保留在该计数中。
            public int CommittedCharacterCount;

            // 本句按下退格键的次数；空文本退格也计数。
            public int BackspaceActionCount;

            // 本句按下 Shift 的次数。
            public int ShiftActionCount;

            // 本句按下 CapsLock 的次数。
            public int CapsLockActionCount;

            // 本句所有修饰键动作数，目前等于 Shift + CapsLock。
            public int ModifierActionCount;

            // 是否通过正常完成当前句进入下一句；超时或手动结束时通常为 false。
            public bool Completed;
        }

        // 一次完整试次的汇总数据；对应 trial_results.csv 中的一行。
        public class TrialResult
        {
            // 当前 CSV 数据结构版本。
            public int SchemaVersion;

            // 本次程序运行编号，同一次启动期间保持不变。
            public string SessionId;

            // 当前试次编号，例如 T001。
            public string TrialId;

            // 试次开始的 UTC 时间，使用 ISO 8601 格式保存。
            public string StartTimestampLocal;

            // 试次结束的 UTC 时间，使用 ISO 8601 格式保存。
            public string EndTimestampLocal;

            // 参与者编号。
            public string ParticipantId;

            // 当前试次使用的输入方式。
            public VRKeyboardInputMode Method;

            // 当前试次是否为练习。
            public bool IsPractice;

            // 当前试次总用时，单位为秒。
            public double DurationSeconds;

            // 原始输入速度，每 5 个字符按一个单词计算。
            public double GrossWPM;

            // 考虑准确率后的有效输入速度。
            public double EffectiveWPM;

            // 整个试次累计的编辑距离。
            public int LevenshteinDistance;

            // 整个试次的字符错误率。
            public double CER;

            // 整个试次的准确率百分比。
            public double AccuracyPercent;

            // 整个试次的物理输入动作总数。
            public int PhysicalActionCount;

            // 整个试次曾经提交过的字符总数。
            public int CommittedCharacterCount;

            // 整个试次的退格动作次数。
            public int BackspaceActionCount;

            // 整个试次的 Shift 动作次数。
            public int ShiftActionCount;

            // 整个试次的 CapsLock 动作次数。
            public int CapsLockActionCount;

            // 整个试次的修饰键动作次数，目前等于 Shift + CapsLock。
            public int ModifierActionCount;

            // 当前试次实际记录的句子数量。
            public int SentenceCount;

            // 当前试次是否有效。
            public bool TrialValid;

            // 当前试次无效时的原因。
            public string InvalidReason;

            // 当前试次包含的全部逐句结果。
            public SentenceResult[] Sentences;
        }

        // 当前试次已经完成记录的逐句结果列表。
        readonly List<SentenceResult> m_SentenceResults = new List<SentenceResult>();

        // 当前试次编号。
        string m_TrialId;

        // 当前试次开始时的 UTC 时间字符串。
        string m_StartTimestampLocal;

        // 是否已经调用 PrepareTrial 完成试次初始化。
        bool m_TrialPrepared;

        // 当前试次是否已经正式开始。
        bool m_TrialStarted;

        // 当前试次是否已经结束并生成结果，防止重复导出。
        bool m_TrialCompleted;

        // 当前句开始时的单调实时时间，用于计算逐句耗时。
        double m_SentenceStartedAt;

        // 以下字段累计整个试次的动作和字符统计。
        int m_PhysicalActionCount;
        int m_CommittedCharacterCount;
        int m_BackspaceActionCount;
        int m_ShiftActionCount;
        int m_CapsLockActionCount;

        // 以下字段只累计当前句，切换到下一句时会清零。
        int m_SentencePhysicalActionCount;
        int m_SentenceCommittedCharacterCount;
        int m_SentenceBackspaceActionCount;
        int m_SentenceShiftActionCount;
        int m_SentenceCapsLockActionCount;

        // 最近一次完成的试次结果，供其他脚本读取。
        public TrialResult lastTrialResult { get; private set; }

        //结果自定义输出路径
        public bool m_UseCustomExportPath = false;
        public string m_CustomExportPath = @"E:\MSC\VRTyping2022\ExperimentResults";


        public string exportDirectory
        {
            get
            {
#if !UNITY_ANDROID || UNITY_EDITOR
                if (m_UseCustomExportPath && !string.IsNullOrWhiteSpace(m_CustomExportPath))//使用自定义路径
                {
                    return m_CustomExportPath;
                }
#endif

                return Path.Combine(Application.persistentDataPath,m_ExportFolderName);
            }
        }

        // 组件初始化时自动查找输入模式选择器。
        void Awake()
        {
            CacheReferences();
            RefreshParticipantIdFromResults();
        }

        // 组件启用时开始监听键盘层发布的物理输入动作。
        void OnEnable()
        {
            if (m_ParticipantIdInputField != null)
                m_ParticipantIdInputField.onEndEdit.AddListener(HandleParticipantIdEndEdit);
            VRKeyboardInputTelemetry.InputStarted += HandleInputStarted;
            VRKeyboardInputTelemetry.PhysicalActionRecorded += HandlePhysicalAction;
        }

        // 组件禁用时取消监听，防止重复订阅或对象销毁后仍收到事件。
        void OnDisable()
        {
            if (m_ParticipantIdInputField != null)
                m_ParticipantIdInputField.onEndEdit.RemoveListener(HandleParticipantIdEndEdit);
            VRKeyboardInputTelemetry.InputStarted -= HandleInputStarted;
            VRKeyboardInputTelemetry.PhysicalActionRecorded -= HandlePhysicalAction;
        }

        // Inspector 未指定输入模式选择器时，从场景中自动寻找一个。
        void CacheReferences()
        {
            if (m_InputModeSelector == null)
                m_InputModeSelector = FindObjectOfType<VRKeyboardInputModeSelector>(true);

            // 场景没有手动挂载时，只接受 ParticipantTXT 子物体中名为 ID 的输入框。
            if (m_ParticipantIdInputField == null)
            {
                var inputFields = FindObjectsOfType<TMP_InputField>(true);
                for (var i = 0; i < inputFields.Length; i++)
                {
                    var inputField = inputFields[i];
                    if (inputField != null &&
                        inputField.name == "ID" &&
                        inputField.transform.parent != null &&
                        inputField.transform.parent.name == "ParticipantTXT")
                    {
                        m_ParticipantIdInputField = inputField;
                        break;
                    }
                }
            }
        }

        // 从整个 trial_results.csv 中寻找已经完成 HandTouch10 正式测试的最大参与者编号。
        // CSV 的物理最后一行不参与判断，因此旧参与者后续追加练习或重测不会改变结果。
        public void RefreshParticipantIdFromResults()
        {
            var nextParticipantNumber = 1;
            var trialPath = Path.Combine(exportDirectory, TrialFileName);

            try
            {
                if (File.Exists(trialPath))
                {
                    var csvRows = ParseCsv(File.ReadAllText(trialPath, Encoding.UTF8));
                    nextParticipantNumber = FindNextParticipantNumber(csvRows);
                }
            }
            catch (Exception exception)
            {
                // 读取失败时保留当前有效编号；当前编号也无效时才回退到 P001。
                Debug.LogWarning("Failed to read participant ID from typing results: " + exception, this);
                if (TryParseParticipantNumber(m_ParticipantId, out var currentNumber))
                    nextParticipantNumber = currentNumber;
            }

            SetParticipantId(FormatParticipantId(nextParticipantNumber));
        }

        // 用户完成编辑时验证并标准化编号。
        // 7、007、P007 和 p007 都会统一保存成 P007。
        void HandleParticipantIdEndEdit(string value)
        {
            if (TryParseParticipantNumber(value, out var participantNumber))
            {
                SetParticipantId(FormatParticipantId(participantNumber));
                return;
            }

            Debug.LogWarning("Participant ID must be a positive number such as 7, 007, or P007.", this);
            SynchronizeParticipantIdInputField();
        }

        // 在试次结束前再读取一次输入框，避免输入框仍有焦点时 onEndEdit 尚未触发。
        void SynchronizeParticipantIdFromInputField()
        {
            if (m_ParticipantIdInputField == null)
                return;

            if (TryParseParticipantNumber(m_ParticipantIdInputField.text, out var participantNumber))
                SetParticipantId(FormatParticipantId(participantNumber));
            else
                SynchronizeParticipantIdInputField();
        }

        void SetParticipantId(string participantId)
        {
            m_ParticipantId = participantId;
            SynchronizeParticipantIdInputField();
        }

        void SynchronizeParticipantIdInputField()
        {
            if (m_ParticipantIdInputField != null && m_ParticipantIdInputField.text != m_ParticipantId)
                m_ParticipantIdInputField.SetTextWithoutNotify(m_ParticipantId);
        }

        static string FormatParticipantId(int participantNumber)
        {
            return "P" + Mathf.Max(1, participantNumber).ToString("D3", s_InvariantCulture);
        }

        static bool TryParseParticipantNumber(string participantId, out int participantNumber)
        {
            participantNumber = 0;
            if (string.IsNullOrWhiteSpace(participantId))
                return false;

            var value = participantId.Trim();
            if (value.Length > 0 && (value[0] == 'P' || value[0] == 'p'))
                value = value.Substring(1);

            return int.TryParse(value, NumberStyles.None, s_InvariantCulture, out participantNumber) &&
                   participantNumber > 0;
        }

        static int FindNextParticipantNumber(List<string[]> csvRows)
        {
            if (csvRows == null || csvRows.Count == 0)
                return 1;

            var header = csvRows[0];
            var participantColumn = FindColumnIndex(header, "ParticipantId");
            var methodColumn = FindColumnIndex(header, "InputMethod");
            var practiceColumn = FindColumnIndex(header, "IsPractice");
            var validColumn = FindColumnIndex(header, "TrialValid");

            if (participantColumn < 0 || methodColumn < 0 || practiceColumn < 0)
                return 1;

            var maximumCompletedParticipant = 0;
            for (var rowIndex = 1; rowIndex < csvRows.Count; rowIndex++)
            {
                var row = csvRows[rowIndex];
                if (!TryGetColumn(row, methodColumn, out var method) ||
                    !string.Equals(method, VRKeyboardInputMode.HandTouch10.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryGetColumn(row, practiceColumn, out var practiceValue) ||
                    !bool.TryParse(practiceValue, out var isPractice) ||
                    isPractice)
                {
                    continue;
                }

                // 旧 CSV 没有 TrialValid 列时按有效处理；新 CSV 明确为 false 时不算完成。
                if (validColumn >= 0 &&
                    (!TryGetColumn(row, validColumn, out var validValue) ||
                     !bool.TryParse(validValue, out var isValid) ||
                     !isValid))
                {
                    continue;
                }

                if (TryGetColumn(row, participantColumn, out var participantId) &&
                    TryParseParticipantNumber(participantId, out var participantNumber))
                {
                    maximumCompletedParticipant = Mathf.Max(maximumCompletedParticipant, participantNumber);
                }
            }

            return maximumCompletedParticipant == int.MaxValue
                ? int.MaxValue
                : maximumCompletedParticipant + 1;
        }

        static int FindColumnIndex(string[] header, string columnName)
        {
            if (header == null)
                return -1;

            for (var i = 0; i < header.Length; i++)
            {
                var value = (header[i] ?? string.Empty).TrimStart('\uFEFF').Trim();
                if (string.Equals(value, columnName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        static bool TryGetColumn(string[] row, int columnIndex, out string value)
        {
            value = string.Empty;
            if (row == null || columnIndex < 0 || columnIndex >= row.Length)
                return false;

            value = (row[columnIndex] ?? string.Empty).Trim();
            return true;
        }

        // 解析由本脚本写出的标准 CSV，正确处理逗号、双引号以及字段内换行。
        static List<string[]> ParseCsv(string csvText)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(csvText))
                return rows;

            var fields = new List<string>();
            var fieldBuilder = new StringBuilder();
            var insideQuotes = false;

            for (var i = 0; i < csvText.Length; i++)
            {
                var character = csvText[i];
                if (i == 0 && character == '\uFEFF')
                    continue;

                if (insideQuotes)
                {
                    if (character == '"')
                    {
                        if (i + 1 < csvText.Length && csvText[i + 1] == '"')
                        {
                            fieldBuilder.Append('"');
                            i++;
                        }
                        else
                        {
                            insideQuotes = false;
                        }
                    }
                    else
                    {
                        fieldBuilder.Append(character);
                    }

                    continue;
                }

                if (character == '"' && fieldBuilder.Length == 0)
                {
                    insideQuotes = true;
                }
                else if (character == ',')
                {
                    fields.Add(fieldBuilder.ToString());
                    fieldBuilder.Clear();
                }
                else if (character == '\r' || character == '\n')
                {
                    fields.Add(fieldBuilder.ToString());
                    fieldBuilder.Clear();
                    AddCsvRowIfNotEmpty(rows, fields);
                    fields.Clear();

                    if (character == '\r' && i + 1 < csvText.Length && csvText[i + 1] == '\n')
                        i++;
                }
                else
                {
                    fieldBuilder.Append(character);
                }
            }

            if (fieldBuilder.Length > 0 || fields.Count > 0)
            {
                fields.Add(fieldBuilder.ToString());
                AddCsvRowIfNotEmpty(rows, fields);
            }

            return rows;
        }

        static void AddCsvRowIfNotEmpty(List<string[]> rows, List<string> fields)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                if (!string.IsNullOrEmpty(fields[i]))
                {
                    rows.Add(fields.ToArray());
                    return;
                }
            }
        }

        // 准备一轮新试次：生成编号、清空旧句子和所有累计计数。
        public void PrepareTrial()
        {
            CacheReferences();
            // 同一次程序运行内按 T001、T002……递增，便于人工查看和筛选。
            m_TrialId = "T" + s_NextTrialNumber.ToString("D3", s_InvariantCulture);
            s_NextTrialNumber++;
            m_StartTimestampLocal = string.Empty;
            m_TrialPrepared = true;
            m_TrialStarted = false;
            m_TrialCompleted = false;
            m_SentenceStartedAt = 0d;
            m_SentenceResults.Clear();
            ResetTrialCounters();
            ResetSentenceCounters();
        }

        // 正式开始当前试次，并记录开始时间；重复调用不会重新计时。
        public void BeginTrial()
        {
            // 外部没有先调用 PrepareTrial 时自动补做初始化。
            if (!m_TrialPrepared)
                PrepareTrial();

            // 已开始或已结束的试次不能再次开始。
            if (m_TrialStarted || m_TrialCompleted)
                return;

            m_TrialStarted = true;

            // realtimeSinceStartupAsDouble 不受 Time.timeScale 影响，适合实验计时。
            m_SentenceStartedAt = Time.realtimeSinceStartupAsDouble;

            // 时间戳使用 UTC ISO 8601，便于跨设备和跨时区分析。
            m_StartTimestampLocal = DateTimeOffset.Now.ToString("O", s_InvariantCulture);
        }

        // 开始记录下一句话，并清空上一句话的独立动作计数。
        public void BeginSentence()
        {
            if (!m_TrialStarted)
                BeginTrial();

            ResetSentenceCounters();
            m_SentenceStartedAt = Time.realtimeSinceStartupAsDouble;
        }

        // 统计实际写入过的字符；后来被退格删除的字符仍保留在该累计值中。
        public void RecordTextChange(string previousText, string currentText)
        {
            // 未准备或已经完成的试次不再接收文本变化。
            if (!m_TrialPrepared || m_TrialCompleted)
                return;

            // 文本先于显式开始信号到达时，自动开始试次。
            if (!m_TrialStarted)
                BeginTrial();

            // 计算本次变化中新插入或替换产生的字符数量。
            var insertedCount = CountInsertedCharacters(previousText, currentText);

            // 同时更新整个试次和当前句的字符累计值。
            m_CommittedCharacterCount += insertedCount;
            m_SentenceCommittedCharacterCount += insertedCount;
        }

        // 保存一句话的最终文本、准确率和该句期间的动作统计。
        public void RecordSentence(
            int sentenceIndex,
            string targetText,
            string finalInputText,
            int targetCharacterCount,
            int finalCharacterCount,
            int levenshteinDistance,
            bool completed)
        {
            // 已经导出的试次不允许继续增加句子。
            if (m_TrialCompleted)
                return;

            // 如果这是当前试次收到的第一条数据，则自动开始试次。
            if (!m_TrialStarted)
                BeginTrial();

            // CER 为字符错误率；目标文本为空时避免除以零。
            var cer = targetCharacterCount == 0
                ? 0d
                : levenshteinDistance / (double)targetCharacterCount;

            // 准确率最低限制为 0%，避免错误率超过 100% 时出现负值。
            var accuracy = Math.Max(0d, 1d - cer) * 100d;

            // 把当前句的快照加入列表，等待试次结束时统一导出。
            m_SentenceResults.Add(new SentenceResult
            {
                SentenceIndex = sentenceIndex,
                TargetText = targetText ?? string.Empty,
                FinalInputText = finalInputText ?? string.Empty,
                DurationSeconds = Math.Max(0d, Time.realtimeSinceStartupAsDouble - m_SentenceStartedAt),
                TargetCharacterCount = targetCharacterCount,
                FinalCharacterCount = finalCharacterCount,
                LevenshteinDistance = levenshteinDistance,
                CER = cer,
                AccuracyPercent = accuracy,
                PhysicalActionCount = m_SentencePhysicalActionCount,
                CommittedCharacterCount = m_SentenceCommittedCharacterCount,
                BackspaceActionCount = m_SentenceBackspaceActionCount,
                ShiftActionCount = m_SentenceShiftActionCount,
                CapsLockActionCount = m_SentenceCapsLockActionCount,
                ModifierActionCount = m_SentenceShiftActionCount + m_SentenceCapsLockActionCount,
                Completed = completed,
            });
        }

        // 完成当前试次，生成汇总结果，并根据设置自动导出 CSV。
        public TrialResult CompleteTrial(
            double durationSeconds,
            double grossWpm,
            double effectiveWpm,
            int levenshteinDistance,
            double cer,
            double accuracyPercent)
        {
            // 防止 FinishTest 被重复调用时写入重复的 CSV 行。
            if (m_TrialCompleted)
                return lastTrialResult;

            // 输入框仍在编辑时也使用它的最新内容作为本次试次编号。
            SynchronizeParticipantIdFromInputField();

            // 没有显式开始时先补齐开始时间。
            if (!m_TrialStarted)
                BeginTrial();

            m_TrialCompleted = true;

            // 优先读取输入模式选择器；不存在时使用 Inspector 中的备用模式。
            var method = m_InputModeSelector != null
                ? m_InputModeSelector.currentInputMode
                : m_FallbackInputMethod;

            // 将整个试次的配置、成绩和累计动作整理为一个结果对象。
            lastTrialResult = new TrialResult
            {
                SchemaVersion = SchemaVersion,
                SessionId = s_SessionId,
                TrialId = m_TrialId,
                StartTimestampLocal = m_StartTimestampLocal,
                EndTimestampLocal = DateTimeOffset.Now.ToString("O", s_InvariantCulture),
                ParticipantId = m_ParticipantId ?? string.Empty,
                Method = method,
                IsPractice = m_IsPractice,
                DurationSeconds = durationSeconds,
                GrossWPM = grossWpm,
                EffectiveWPM = effectiveWpm,
                LevenshteinDistance = levenshteinDistance,
                CER = cer,
                AccuracyPercent = accuracyPercent,
                PhysicalActionCount = m_PhysicalActionCount,
                CommittedCharacterCount = m_CommittedCharacterCount,
                BackspaceActionCount = m_BackspaceActionCount,
                ShiftActionCount = m_ShiftActionCount,
                CapsLockActionCount = m_CapsLockActionCount,
                ModifierActionCount = m_ShiftActionCount + m_CapsLockActionCount,
                SentenceCount = m_SentenceResults.Count,
                TrialValid = m_TrialValid,
                InvalidReason = m_InvalidReason ?? string.Empty,
                Sentences = m_SentenceResults.ToArray(),
            };

            // Inspector 中开启自动导出时，测试结束立即追加到 CSV。
            var exportSucceeded = m_AutoExportCsv && ExportCsv(lastTrialResult);

            // HandTouch10 是六种输入方式中的最后一种。
            // 有效正式结果成功写入后，重新扫描整个 CSV 并显示下一位参与者编号。
            if (exportSucceeded &&
                method == VRKeyboardInputMode.HandTouch10 &&
                !m_IsPractice &&
                m_TrialValid)
            {
                RefreshParticipantIdFromResults();
            }

            return lastTrialResult;
        }

        // 接收键盘层发布的物理动作，并同时累计试次级和句子级计数。
        void HandleInputStarted()
        {
            // Start the trial at the beginning of a valid Swipe trajectory without
            // counting an extra physical action. The completed Swipe is counted later.
            if (m_TrialPrepared && !m_TrialCompleted && !m_TrialStarted)
                BeginTrial();
        }

        void HandlePhysicalAction(VRKeyboardPhysicalActionKind kind)
        {
            // 只统计已经准备且尚未完成的试次。
            if (!m_TrialPrepared || m_TrialCompleted)
                return;

            // 第一次动作可以直接启动试次，包括空文本退格、Shift 和 CapsLock。
            if (!m_TrialStarted)
                BeginTrial();

            // 所有动作类型都会进入物理动作总数。
            m_PhysicalActionCount++;
            m_SentencePhysicalActionCount++;

            // 退格和修饰键另外保存分类计数，便于实验分析。
            switch (kind)
            {
                case VRKeyboardPhysicalActionKind.Backspace:
                    m_BackspaceActionCount++;
                    m_SentenceBackspaceActionCount++;
                    break;
                case VRKeyboardPhysicalActionKind.Shift:
                    m_ShiftActionCount++;
                    m_SentenceShiftActionCount++;
                    break;
                case VRKeyboardPhysicalActionKind.CapsLock:
                    m_CapsLockActionCount++;
                    m_SentenceCapsLockActionCount++;
                    break;
            }
        }

        bool ExportCsv(TrialResult trial)
        {
            try
            {
                // 第一次导出时自动创建结果文件夹。
                Directory.CreateDirectory(exportDirectory);

                // 汇总数据和逐句数据分别保存到两个 CSV 文件。
                var trialPath = Path.Combine(exportDirectory, TrialFileName);
                var sentencePath = Path.Combine(exportDirectory, SentenceFileName);

                // 每次试次只向汇总表追加一行。
                AppendCsvRow(trialPath, TrialHeader(), TrialRow(trial));

                // 每个句子向逐句明细表追加一行，并通过 TrialId 与汇总表关联。
                for (var i = 0; i < trial.Sentences.Length; i++)
                    AppendCsvRow(sentencePath, SentenceHeader(), SentenceRow(trial, trial.Sentences[i]));

                // 输出完整目录，方便在 Unity Console 中直接找到结果。
                if (m_LogExportPath)
                    Debug.Log("Typing test CSV exported to: " + exportDirectory, this);

                return true;
            }
            catch (Exception exception)
            {
                // 文件被占用、目录无权限等写入错误会显示在 Console 中，不让游戏崩溃。
                Debug.LogError("Failed to export typing test CSV: " + exception, this);
                return false;
            }
        }

        // 如果文件不存在则先写表头，然后把一行数据追加到文件末尾。
        static void AppendCsvRow(string path, string header, string row)
        {
            // 新文件使用带 BOM 的 UTF-8，保证 Excel 正确显示中文。
            if (!File.Exists(path))
                File.WriteAllText(path, header + Environment.NewLine, s_Utf8WithBom);

            // 后续数据不重复写 BOM，只追加当前行。
            File.AppendAllText(path, row + Environment.NewLine, new UTF8Encoding(false));
        }

        // 定义 trial_results.csv 的列名及列顺序。
        static string TrialHeader()
        {
            return Csv(
                "SchemaVersion", "SessionId", "TrialId", "StartTimestampLocal", "EndTimestampLocal",
                "ParticipantId", "InputMethod", "IsPractice", "DurationSeconds", "GrossWPM",
                "EffectiveWPM", "LevenshteinDistance", "CER", "AccuracyPercent",
                "PhysicalActionCount", "CommittedCharacterCount", "BackspaceActionCount",
                "ShiftActionCount", "CapsLockActionCount", "ModifierActionCount",
                "SentenceCount", "TrialValid", "InvalidReason");
        }

        // 按 TrialHeader 的相同顺序，把试次结果转换成一行 CSV。
        static string TrialRow(TrialResult value)
        {
            return Csv(
                value.SchemaVersion.ToString(s_InvariantCulture), value.SessionId, value.TrialId,
                value.StartTimestampLocal, value.EndTimestampLocal, value.ParticipantId, value.Method.ToString(),
                Bool(value.IsPractice), Number(value.DurationSeconds), Number(value.GrossWPM),
                Number(value.EffectiveWPM), value.LevenshteinDistance.ToString(s_InvariantCulture),
                Number(value.CER), Number(value.AccuracyPercent),
                value.PhysicalActionCount.ToString(s_InvariantCulture),
                value.CommittedCharacterCount.ToString(s_InvariantCulture),
                value.BackspaceActionCount.ToString(s_InvariantCulture),
                value.ShiftActionCount.ToString(s_InvariantCulture),
                value.CapsLockActionCount.ToString(s_InvariantCulture),
                value.ModifierActionCount.ToString(s_InvariantCulture),
                value.SentenceCount.ToString(s_InvariantCulture), Bool(value.TrialValid), value.InvalidReason);
        }

        // 定义 sentence_results.csv 的列名及列顺序。
        static string SentenceHeader()
        {
            return Csv(
                "SchemaVersion", "SessionId", "TrialId", "ParticipantId", "InputMethod", "IsPractice",
                "SentenceIndex", "TargetText", "FinalInputText", "DurationSeconds",
                "TargetCharacterCount", "FinalCharacterCount", "LevenshteinDistance", "CER",
                "AccuracyPercent", "PhysicalActionCount", "CommittedCharacterCount",
                "BackspaceActionCount", "ShiftActionCount", "CapsLockActionCount",
                "ModifierActionCount", "Completed");
        }

        // 按 SentenceHeader 的相同顺序，把一个句子结果转换成一行 CSV。
        static string SentenceRow(TrialResult trial, SentenceResult value)
        {
            return Csv(
                trial.SchemaVersion.ToString(s_InvariantCulture), trial.SessionId, trial.TrialId,
                trial.ParticipantId, trial.Method.ToString(), Bool(trial.IsPractice),
                value.SentenceIndex.ToString(s_InvariantCulture), value.TargetText, value.FinalInputText,
                Number(value.DurationSeconds), value.TargetCharacterCount.ToString(s_InvariantCulture),
                value.FinalCharacterCount.ToString(s_InvariantCulture),
                value.LevenshteinDistance.ToString(s_InvariantCulture), Number(value.CER),
                Number(value.AccuracyPercent), value.PhysicalActionCount.ToString(s_InvariantCulture),
                value.CommittedCharacterCount.ToString(s_InvariantCulture),
                value.BackspaceActionCount.ToString(s_InvariantCulture),
                value.ShiftActionCount.ToString(s_InvariantCulture),
                value.CapsLockActionCount.ToString(s_InvariantCulture),
                value.ModifierActionCount.ToString(s_InvariantCulture), Bool(value.Completed));
        }

        // 将多个字段拼成符合 CSV 规则的一行。
        static string Csv(params string[] values)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < values.Length; i++)
            {
                // 字段之间用英文逗号分隔。
                if (i > 0)
                    builder.Append(',');

                var value = values[i] ?? string.Empty;

                // 每个字段都使用双引号包裹；字段内部的双引号按 CSV 规则写成两个双引号。
                builder.Append('"');
                builder.Append(value.Replace("\"", "\"\""));
                builder.Append('"');
            }

            return builder.ToString();
        }

        // 使用固定小数点格式输出数值，最多保留六位小数。
        static string Number(double value)
        {
            return value.ToString("0.######", s_InvariantCulture);
        }

        // 把布尔值统一写成小写 true/false。
        static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        // 计算从旧文本变到新文本时插入或替换产生了多少个新字符。
        static int CountInsertedCharacters(string previousText, string currentText)
        {
            // 空引用按空字符串处理。
            previousText = previousText ?? string.Empty;
            currentText = currentText ?? string.Empty;

            // 新文本为空表示没有提交新字符。
            if (currentText.Length == 0)
                return 0;

            // 从空文本开始时，新文本的全部字符都属于新提交字符。
            if (previousText.Length == 0)
                return currentText.Length;

            // 新文本长度减去最长公共子序列长度，即本次编辑实际插入/替换出的字符数。
            var previous = new int[currentText.Length + 1];
            var current = new int[currentText.Length + 1];
            for (var i = 1; i <= previousText.Length; i++)
            {
                for (var j = 1; j <= currentText.Length; j++)
                {
                    current[j] = previousText[i - 1] == currentText[j - 1]
                        ? previous[j - 1] + 1
                        : Math.Max(previous[j], current[j - 1]);
                }

                var swap = previous;
                previous = current;
                current = swap;
                Array.Clear(current, 0, current.Length);
            }

            return currentText.Length - previous[currentText.Length];
        }

        // 清空整个试次的累计动作和字符计数。
        void ResetTrialCounters()
        {
            m_PhysicalActionCount = 0;
            m_CommittedCharacterCount = 0;
            m_BackspaceActionCount = 0;
            m_ShiftActionCount = 0;
            m_CapsLockActionCount = 0;
        }

        // 清空当前句的累计动作和字符计数。
        void ResetSentenceCounters()
        {
            m_SentencePhysicalActionCount = 0;
            m_SentenceCommittedCharacterCount = 0;
            m_SentenceBackspaceActionCount = 0;
            m_SentenceShiftActionCount = 0;
            m_SentenceCapsLockActionCount = 0;
        }
    }
}
