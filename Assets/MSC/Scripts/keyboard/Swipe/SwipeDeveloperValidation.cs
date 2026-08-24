using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using VRTyping.Tests;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace VRTyping.Keyboard
{

    public sealed class SwipeDeveloperValidation : MonoBehaviour
    {
        const string ValidationFolderName = "SwipeValidation";
        const string WordListFileName = "swipe_validation_words.txt";
        const int DefaultRepetitions = 5;
        const int CandidateCount = 5;
        const float ControllerToggleHoldSeconds = 2f;

        static readonly BindingFlags s_InstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly string[] s_FallbackWords =
        {
            "accuracy", "afternoon", "bright", "careful", "comfort",
            "different", "dropped", "error", "familiar", "final",
            "glass", "higher", "important", "input", "keyboard",
            "light", "making", "methods", "practice", "quickly",
            "reality", "rubber", "sentence", "speed", "testing",
            "typing", "virtual", "window", "while", "which",
        };

        readonly Dictionary<object, TraceSnapshot> m_TraceSnapshots =
            new Dictionary<object, TraceSnapshot>();
        readonly List<TargetAttempt> m_Schedule = new List<TargetAttempt>();
        readonly List<double> m_RecognitionTimesMs = new List<double>();
        readonly List<BehaviourState> m_SuspendedBehaviours = new List<BehaviourState>();
        readonly List<GameObjectState> m_HiddenObjects = new List<GameObjectState>();

        VRKeyboardSwipeInput m_SwipeInput;
        SwipeTypingRecognizer m_Recognizer;
        VRKeyboardInputModeSelector m_ModeSelector;
        VRKeyboardInputMode m_PreviousInputMode;
        bool m_HasPreviousInputMode;

        FieldInfo m_ActiveTracesField;
        FieldInfo m_RecognizerField;
        FieldInfo m_TraceGesturePointsField;

        bool m_Active;
        bool m_ControllerToggleLatched;
        float m_ControllerToggleStartedAt = -1f;
        bool m_ReflectionFailureReported;
        int m_ScheduleIndex;
        int m_Top1Correct;
        int m_Top5Correct;
        int m_NoCandidateCount;
        int m_RandomSeed;
        string m_SessionId;
        string m_SessionDirectory;
        string m_ResultPath;
        string m_CandidatePath;
        string m_PointPath;
        string m_SummaryPath;
        string m_LastResult = "No validation attempt recorded.";

        GameObject m_OverlayRoot;
        TMP_Text m_OverlayText;

        public bool isValidationActive => m_Active;
        public string sessionDirectory => m_SessionDirectory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindObjectOfType<SwipeDeveloperValidation>(true) != null)
                return;

            var root = new GameObject("[Swipe Developer Validation]");
            DontDestroyOnLoad(root);
            root.AddComponent<SwipeDeveloperValidation>();
        }

        void Awake()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            if (HasCommandLineFlag("--swipe-validation"))
                StartCoroutine(ActivateAfterSceneIsReady());
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (m_Active)
                DeactivateValidation();
        }

        void OnApplicationQuit()
        {
            if (m_Active)
                WriteSummary();
        }

        void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ClearRuntimeReferences();
            if (m_Active)
            {
                WriteSummary();
                RestoreProductionState();
                m_Active = false;
                StartCoroutine(ActivateAfterSceneIsReady());
            }
        }

        IEnumerator ActivateAfterSceneIsReady()
        {
            yield return null;
            ActivateValidation();
        }

        void Update()
        {
            UpdateToggleInput();
            if (!m_Active)
                return;

            if (!EnsureRuntimeReferences())
            {
                RefreshOverlay();
                return;
            }

            CaptureActiveTraces();
            RefreshOverlay();
        }

        void LateUpdate()
        {
            if (!m_Active || m_SwipeInput == null || m_ActiveTracesField == null)
                return;

            CaptureActiveTraces();
        }

        void OnGUI()
        {
            if (!m_Active)
                return;

            var width = Mathf.Min(720f, Screen.width - 30f);
            var height = 250f;
            var rect = new Rect(15f, 15f, width, height);
            GUI.Box(rect, GUIContent.none);

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
            };

            GUI.Label(
                new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, rect.height - 16f),
                BuildOverlayText(),
                style);
        }

        void UpdateToggleInput()
        {
            if (UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.f8Key.wasPressedThisFrame)
            {
                ToggleValidation();
                return;
            }

            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var held = IsToggleButtonCombinationHeld(left) && IsToggleButtonCombinationHeld(right);

            if (!held)
            {
                m_ControllerToggleStartedAt = -1f;
                m_ControllerToggleLatched = false;
                return;
            }

            if (m_ControllerToggleLatched)
                return;

            if (m_ControllerToggleStartedAt < 0f)
            {
                m_ControllerToggleStartedAt = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - m_ControllerToggleStartedAt < ControllerToggleHoldSeconds)
                return;

            m_ControllerToggleLatched = true;
            ToggleValidation();
        }

        static bool IsToggleButtonCombinationHeld(UnityEngine.XR.InputDevice device)
        {
            if (!device.isValid)
                return false;

            return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out var grip) && grip &&
                   device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out var primary) && primary;
        }

        void ToggleValidation()
        {
            if (m_Active)
                DeactivateValidation();
            else
                ActivateValidation();
        }

        void ActivateValidation()
        {
            if (m_Active)
                return;

            if (!EnsureSwipeInputReference())
            {
                Debug.LogWarning("Swipe validation could not start because VRKeyboardSwipeInput was not found.", this);
                return;
            }

            m_Active = true;
            m_ReflectionFailureReported = false;
            m_TraceSnapshots.Clear();
            m_RecognitionTimesMs.Clear();
            m_Top1Correct = 0;
            m_Top5Correct = 0;
            m_NoCandidateCount = 0;
            m_ScheduleIndex = 0;
            m_LastResult = "Waiting for the first swipe.";

            SuspendProductionState();
            SetSwipeMode();
            if (!EnsureRuntimeReferences())
            {
                m_Active = false;
                RestoreProductionState();
                Debug.LogWarning("Swipe validation could not start because SwipeTypingRecognizer was not available.", this);
                return;
            }
            StartNewSession();
            CreateVrOverlay();
            RefreshOverlay();

            Debug.Log("Swipe developer validation started. Output: " + m_SessionDirectory, this);
        }

        void DeactivateValidation()
        {
            if (!m_Active)
                return;

            WriteSummary();
            m_Active = false;
            m_TraceSnapshots.Clear();

            if (m_SwipeInput != null)
            {
                m_SwipeInput.TryHandleCandidateSelection("ESC");
                m_SwipeInput.ClearText();
            }

            DestroyVrOverlay();
            RestoreProductionState();
            Debug.Log("Swipe developer validation stopped. Output: " + m_SessionDirectory, this);
        }

        bool EnsureRuntimeReferences()
        {
            if (!EnsureSwipeInputReference())
                return false;

            if (m_ActiveTracesField == null)
            {
                m_ActiveTracesField = typeof(VRKeyboardSwipeInput).GetField("m_ActiveTraces", s_InstanceFields);
                m_RecognizerField = typeof(VRKeyboardSwipeInput).GetField("m_SwipeRecognizer", s_InstanceFields);
            }

            if (m_Recognizer == null)
            {
                if (m_RecognizerField != null)
                    m_Recognizer = m_RecognizerField.GetValue(m_SwipeInput) as SwipeTypingRecognizer;
                if (m_Recognizer == null)
                    m_Recognizer = m_SwipeInput.GetComponent<SwipeTypingRecognizer>();
            }

            if (m_ActiveTracesField == null || m_RecognizerField == null)
            {
                ReportReflectionFailure("Required Swipe fields were not found.");
                return false;
            }

            return m_Recognizer != null;
        }

        bool EnsureSwipeInputReference()
        {
            if (m_SwipeInput == null)
                m_SwipeInput = FindObjectOfType<VRKeyboardSwipeInput>(true);
            return m_SwipeInput != null;
        }

        void ClearRuntimeReferences()
        {
            m_SwipeInput = null;
            m_Recognizer = null;
            m_ModeSelector = null;
            m_TraceGesturePointsField = null;
            m_TraceSnapshots.Clear();
        }

        void CaptureActiveTraces()
        {
            var dictionary = m_ActiveTracesField.GetValue(m_SwipeInput) as IDictionary;
            if (dictionary == null)
            {
                ReportReflectionFailure("The active Swipe trace dictionary could not be read.");
                return;
            }

            var currentTraces = new HashSet<object>();
            foreach (DictionaryEntry entry in dictionary)
            {
                var trace = entry.Value;
                if (trace == null)
                    continue;

                currentTraces.Add(trace);
                var snapshot = CopyTrace(trace);
                if (snapshot != null && snapshot.points.Count > 0)
                    m_TraceSnapshots[trace] = snapshot;
            }

            if (m_TraceSnapshots.Count == 0)
                return;

            var completed = new List<object>();
            foreach (var pair in m_TraceSnapshots)
            {
                if (!currentTraces.Contains(pair.Key))
                    completed.Add(pair.Key);
            }

            for (var i = 0; i < completed.Count; i++)
            {
                var trace = completed[i];
                var snapshot = m_TraceSnapshots[trace];
                m_TraceSnapshots.Remove(trace);

                if (snapshot.points.Count >= 2)
                    RecordAttempt(snapshot);
            }
        }

        TraceSnapshot CopyTrace(object trace)
        {
            if (trace == null)
                return null;

            if (m_TraceGesturePointsField == null || m_TraceGesturePointsField.DeclaringType != trace.GetType())
                m_TraceGesturePointsField = trace.GetType().GetField("gesturePoints", s_InstanceFields);

            if (m_TraceGesturePointsField == null)
            {
                ReportReflectionFailure("SwipeTrace.gesturePoints could not be read.");
                return null;
            }

            var source = m_TraceGesturePointsField.GetValue(trace) as IEnumerable;
            if (source == null)
                return null;

            var snapshot = new TraceSnapshot();
            foreach (var value in source)
            {
                if (value is GesturePoint point)
                    snapshot.points.Add(point);
            }
            return snapshot;
        }

        void RecordAttempt(TraceSnapshot trace)
        {
            if (m_ScheduleIndex >= m_Schedule.Count || m_Recognizer == null)
                return;

            var target = m_Schedule[m_ScheduleIndex];
            var stopwatch = Stopwatch.StartNew();
            var candidates = m_Recognizer.Recognize(trace.points, CandidateCount);
            stopwatch.Stop();

            var recognitionMs = stopwatch.Elapsed.TotalMilliseconds;
            m_RecognitionTimesMs.Add(recognitionMs);

            var correctRank = 0;
            var targetScore = double.NaN;
            for (var i = 0; i < candidates.Count; i++)
            {
                if (!string.Equals(candidates[i].word, target.word, StringComparison.OrdinalIgnoreCase))
                    continue;

                correctRank = i + 1;
                targetScore = candidates[i].finalScore;
                break;
            }

            if (correctRank == 1)
                m_Top1Correct++;
            if (correctRank >= 1 && correctRank <= CandidateCount)
                m_Top5Correct++;
            if (candidates.Count == 0)
                m_NoCandidateCount++;

            var runId = "R" + (m_ScheduleIndex + 1).ToString("D4", CultureInfo.InvariantCulture);
            AppendResultRow(runId, target, trace, candidates, correctRank, targetScore, recognitionMs);
            AppendCandidateRows(runId, candidates);
            AppendPointRows(runId, trace.points);

            var top1 = candidates.Count > 0 ? candidates[0].word : "<none>";
            m_LastResult =
                "Target: " + target.word +
                " | Top-1: " + top1 +
                " | Correct rank: " + (correctRank == 0 ? "outside Top-5" : correctRank.ToString()) +
                " | Recognition: " + recognitionMs.ToString("F2", CultureInfo.InvariantCulture) + " ms";

            m_ScheduleIndex++;
            WriteSummary();

            // Validation output remains completely separate from the formal trial output.
            // Dismiss the production candidate preview and clear any fallback text before
            // the next prompted word.
            if (m_SwipeInput != null)
            {
                m_SwipeInput.TryHandleCandidateSelection("ESC");
                m_SwipeInput.ClearText();
            }

            RefreshOverlay();
        }

        void StartNewSession()
        {
            m_SessionId = "DEV_" + DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            var root = Path.Combine(Application.persistentDataPath, ValidationFolderName);
            Directory.CreateDirectory(root);
            m_SessionDirectory = Path.Combine(root, m_SessionId);
            Directory.CreateDirectory(m_SessionDirectory);

            m_ResultPath = Path.Combine(m_SessionDirectory, "swipe_validation_results.csv");
            m_CandidatePath = Path.Combine(m_SessionDirectory, "swipe_validation_candidates.csv");
            m_PointPath = Path.Combine(m_SessionDirectory, "swipe_validation_points.csv");
            m_SummaryPath = Path.Combine(m_SessionDirectory, "swipe_validation_summary.csv");

            WriteUtf8(
                m_ResultPath,
                Csv(
                    "SessionId", "RunId", "TimestampUtc", "TargetWord", "Repetition", "WordLength",
                    "PointCount", "GestureDurationMs", "RecognitionTimeMs", "Top1", "Top2", "Top3",
                    "Top4", "Top5", "CorrectRank", "Top1FinalScore", "TargetFinalScore",
                    "Top1Confidence", "ParameterSetId") + Environment.NewLine);
            WriteUtf8(
                m_CandidatePath,
                Csv(
                    "SessionId", "RunId", "Rank", "Word", "FinalScore", "Confidence", "OrderedKeyScore",
                    "KeyProbabilityScore", "DtwScore", "StartScore", "EndScore", "DirectionScore",
                    "PathPenalty", "FrequencyBonus", "SpeedReward") + Environment.NewLine);
            WriteUtf8(
                m_PointPath,
                Csv("SessionId", "RunId", "PointIndex", "X", "Y", "RelativeTimeSeconds") + Environment.NewLine);

            var words = LoadValidationWords(root);
            var repetitions = ReadCommandLineInt("--swipe-validation-repetitions=", DefaultRepetitions, 1, 20);
            m_RandomSeed = unchecked((int)DateTime.UtcNow.Ticks);
            BuildSchedule(words, repetitions, m_RandomSeed);
            WriteSessionManifest(words, repetitions);
            WriteParameterSnapshot();
            WriteSummary();
        }

        List<string> LoadValidationWords(string validationRoot)
        {
            var externalPath = Path.Combine(validationRoot, WordListFileName);
            var words = new List<string>();

            if (File.Exists(externalPath))
            {
                AddNormalizedWords(File.ReadAllLines(externalPath), words);
            }
            else
            {
                var session = FindObjectOfType<TypingTestSession>(true);
                if (session != null && session.m_Sentences != null)
                    AddNormalizedWords(session.m_Sentences, words);

                if (words.Count == 0)
                    AddNormalizedWords(s_FallbackWords, words);

                WriteUtf8(externalPath, string.Join(Environment.NewLine, words) + Environment.NewLine);
            }

            if (words.Count == 0)
                AddNormalizedWords(s_FallbackWords, words);

            return words;
        }

        static void AddNormalizedWords(IEnumerable<string> source, List<string> destination)
        {
            var seen = new HashSet<string>(destination, StringComparer.OrdinalIgnoreCase);
            foreach (var line in source)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var builder = new StringBuilder();
                for (var i = 0; i <= line.Length; i++)
                {
                    var value = i < line.Length ? char.ToLowerInvariant(line[i]) : ' ';
                    if (value >= 'a' && value <= 'z')
                    {
                        builder.Append(value);
                        continue;
                    }

                    AddWord(builder);
                }
            }

            void AddWord(StringBuilder builder)
            {
                if (builder.Length < 3)
                {
                    builder.Length = 0;
                    return;
                }

                var word = builder.ToString();
                builder.Length = 0;
                if (seen.Add(word))
                    destination.Add(word);
            }
        }

        void BuildSchedule(IReadOnlyList<string> words, int repetitions, int seed)
        {
            m_Schedule.Clear();
            for (var repetition = 1; repetition <= repetitions; repetition++)
            {
                for (var i = 0; i < words.Count; i++)
                    m_Schedule.Add(new TargetAttempt(words[i], repetition));
            }

            var random = new System.Random(seed);
            for (var i = m_Schedule.Count - 1; i > 0; i--)
            {
                var swapIndex = random.Next(i + 1);
                var temporary = m_Schedule[i];
                m_Schedule[i] = m_Schedule[swapIndex];
                m_Schedule[swapIndex] = temporary;
            }

            // Avoid immediate repetition where possible without changing the selected set.
            for (var i = 1; i < m_Schedule.Count; i++)
            {
                if (!string.Equals(m_Schedule[i - 1].word, m_Schedule[i].word, StringComparison.OrdinalIgnoreCase))
                    continue;

                for (var j = i + 1; j < m_Schedule.Count; j++)
                {
                    if (string.Equals(m_Schedule[i - 1].word, m_Schedule[j].word, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var temporary = m_Schedule[i];
                    m_Schedule[i] = m_Schedule[j];
                    m_Schedule[j] = temporary;
                    break;
                }
            }
        }

        void AppendResultRow(
            string runId,
            TargetAttempt target,
            TraceSnapshot trace,
            IReadOnlyList<SwipeCandidate> candidates,
            int correctRank,
            double targetScore,
            double recognitionMs)
        {
            var topWords = new string[CandidateCount];
            for (var i = 0; i < topWords.Length; i++)
                topWords[i] = i < candidates.Count ? candidates[i].word : string.Empty;

            var gestureDurationMs = trace.points.Count > 1
                ? Math.Max(0d, trace.points[trace.points.Count - 1].time - trace.points[0].time) * 1000d
                : 0d;

            var top1Score = candidates.Count > 0 ? Number(candidates[0].finalScore) : string.Empty;
            var top1Confidence = candidates.Count > 0 ? Number(candidates[0].confidence) : string.Empty;
            var targetScoreText = double.IsNaN(targetScore) ? string.Empty : Number(targetScore);

            AppendUtf8(
                m_ResultPath,
                Csv(
                    m_SessionId,
                    runId,
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    target.word,
                    target.repetition.ToString(CultureInfo.InvariantCulture),
                    target.word.Length.ToString(CultureInfo.InvariantCulture),
                    trace.points.Count.ToString(CultureInfo.InvariantCulture),
                    Number(gestureDurationMs),
                    Number(recognitionMs),
                    topWords[0], topWords[1], topWords[2], topWords[3], topWords[4],
                    correctRank.ToString(CultureInfo.InvariantCulture),
                    top1Score,
                    targetScoreText,
                    top1Confidence,
                    "CURRENT") + Environment.NewLine);
        }

        void AppendCandidateRows(string runId, IReadOnlyList<SwipeCandidate> candidates)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                builder.AppendLine(Csv(
                    m_SessionId,
                    runId,
                    (i + 1).ToString(CultureInfo.InvariantCulture),
                    candidate.word,
                    Number(candidate.finalScore),
                    Number(candidate.confidence),
                    Number(candidate.orderedKeyScore),
                    Number(candidate.keyProbabilityScore),
                    Number(candidate.dtwScore),
                    Number(candidate.startScore),
                    Number(candidate.endScore),
                    Number(candidate.directionScore),
                    Number(candidate.pathPenalty),
                    Number(candidate.frequencyBonus),
                    Number(candidate.speedReward)));
            }

            if (builder.Length > 0)
                AppendUtf8(m_CandidatePath, builder.ToString());
        }

        void AppendPointRows(string runId, IReadOnlyList<GesturePoint> points)
        {
            if (points == null || points.Count == 0)
                return;

            var builder = new StringBuilder();
            var firstTime = points[0].time;
            for (var i = 0; i < points.Count; i++)
            {
                builder.AppendLine(Csv(
                    m_SessionId,
                    runId,
                    i.ToString(CultureInfo.InvariantCulture),
                    Number(points[i].position.x),
                    Number(points[i].position.y),
                    Number(Math.Max(0d, points[i].time - firstTime))));
            }
            AppendUtf8(m_PointPath, builder.ToString());
        }

        void WriteSummary()
        {
            if (string.IsNullOrEmpty(m_SummaryPath))
                return;

            var completed = m_ScheduleIndex;
            var top1Accuracy = completed == 0 ? 0d : m_Top1Correct / (double)completed;
            var top5Accuracy = completed == 0 ? 0d : m_Top5Correct / (double)completed;
            var median = Percentile(m_RecognitionTimesMs, 0.50d);
            var p95 = Percentile(m_RecognitionTimesMs, 0.95d);

            var builder = new StringBuilder();
            builder.AppendLine(Csv(
                "SessionId", "CompletedGestures", "ScheduledGestures", "Top1Correct", "Top1Accuracy",
                "Top5Correct", "Top5Accuracy", "NoCandidateGestures", "MedianRecognitionMs",
                "P95RecognitionMs", "RandomSeed", "Status"));
            builder.AppendLine(Csv(
                m_SessionId,
                completed.ToString(CultureInfo.InvariantCulture),
                m_Schedule.Count.ToString(CultureInfo.InvariantCulture),
                m_Top1Correct.ToString(CultureInfo.InvariantCulture),
                Number(top1Accuracy),
                m_Top5Correct.ToString(CultureInfo.InvariantCulture),
                Number(top5Accuracy),
                m_NoCandidateCount.ToString(CultureInfo.InvariantCulture),
                Number(median),
                Number(p95),
                m_RandomSeed.ToString(CultureInfo.InvariantCulture),
                completed >= m_Schedule.Count && m_Schedule.Count > 0 ? "Complete" : "InProgress"));
            WriteUtf8(m_SummaryPath, builder.ToString());
        }

        void WriteParameterSnapshot()
        {
            if (m_Recognizer == null || string.IsNullOrEmpty(m_SessionDirectory))
                return;

            var path = Path.Combine(m_SessionDirectory, "swipe_validation_parameters.csv");
            var fields = new List<KeyValuePair<string, string>>
            {
                Pair("ParameterSetId", "CURRENT"),
                Pair("UnityVersion", Application.unityVersion),
                Pair("ApplicationVersion", Application.version),
                Pair("Platform", Application.platform.ToString()),
                Pair("Scene", SceneManager.GetActiveScene().name),
                Pair("MaxWords", m_Recognizer.m_MaxWords),
                Pair("ResampleCount", m_Recognizer.m_ResampleCount),
                Pair("MinimumPointDistance", m_Recognizer.m_MinDistance),
                Pair("MovingAverageRadius", m_Recognizer.m_MovingAverageRadius),
                Pair("OutlierSigma", m_Recognizer.m_OutlierSigma),
                Pair("NormalizeRotation", m_Recognizer.m_NormalizeRotation),
                Pair("KeyRadius", m_Recognizer.m_KeyRadius),
                Pair("KeyProbabilitySigma", m_Recognizer.m_KeyProbabilitySigma),
                Pair("MaximumCandidateTemplates", m_Recognizer.m_MaxCandidateTemplates),
                Pair("MaximumFullyScoredCandidates", m_Recognizer.m_MaxFullyScoredCandidates),
                Pair("SoftEndpointLetterCount", m_Recognizer.m_SoftEndpointLetterCount),
                Pair("StartMismatchPenalty", m_Recognizer.m_StartMismatchPenalty),
                Pair("EndMismatchPenalty", m_Recognizer.m_EndMismatchPenalty),
                Pair("DtwWindowRadius", m_Recognizer.m_DtwWindowRadius),
                Pair("OrderedKeyWeight", m_Recognizer.m_Weights.orderedKey),
                Pair("DtwWeight", m_Recognizer.m_Weights.dtw),
                Pair("KeyProbabilityWeight", m_Recognizer.m_Weights.keyProbability),
                Pair("StartWeight", m_Recognizer.m_Weights.start),
                Pair("EndWeight", m_Recognizer.m_Weights.end),
                Pair("DirectionWeight", m_Recognizer.m_Weights.direction),
                Pair("PathPenaltyWeight", m_Recognizer.m_Weights.pathPenalty),
                Pair("WordFrequencyWeight", m_Recognizer.m_Weights.wordFrequency),
                Pair("SpeedRewardWeight", m_Recognizer.m_Weights.speedReward),
                Pair("CaptureMinimumDistance", ReadPrivateField(m_SwipeInput, "m_MinTrajectorySampleDistance")),
                Pair("CaptureMaximumPoints", ReadPrivateField(m_SwipeInput, "m_MaxTrajectoryPoints")),
                Pair("CaptureEndDelay", ReadPrivateField(m_SwipeInput, "m_EndSwipeDelay")),
                Pair("RepeatedLetterDwell", ReadPrivateField(m_SwipeInput, "m_RepeatedLetterDwellTime")),
            };

            if (m_SwipeInput.TryGetKeyboardLayout(out var layout) && layout != null)
                fields.Add(Pair("KeyboardLayoutSignature", layout.signature));

            var builder = new StringBuilder();
            builder.AppendLine(Csv("Parameter", "Value"));
            for (var i = 0; i < fields.Count; i++)
                builder.AppendLine(Csv(fields[i].Key, fields[i].Value));
            WriteUtf8(path, builder.ToString());
        }

        void WriteSessionManifest(IReadOnlyList<string> words, int repetitions)
        {
            var path = Path.Combine(m_SessionDirectory, "swipe_validation_manifest.txt");
            var builder = new StringBuilder();
            builder.AppendLine("SessionId=" + m_SessionId);
            builder.AppendLine("CreatedUtc=" + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendLine("DeveloperId=DEV");
            builder.AppendLine("RepetitionsPerWord=" + repetitions.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("WordCount=" + words.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("ScheduledGestures=" + m_Schedule.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("RandomSeed=" + m_RandomSeed.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("FormalParticipantDataUsedForTuning=false");
            builder.AppendLine();
            builder.AppendLine("Words:");
            for (var i = 0; i < words.Count; i++)
                builder.AppendLine(words[i]);
            WriteUtf8(path, builder.ToString());
        }

        void SuspendProductionState()
        {
            m_SuspendedBehaviours.Clear();
            m_HiddenObjects.Clear();

            Suspend(FindObjectOfType<TypingTestSession>(true));
            Suspend(FindObjectOfType<ResultOutput>(true));

            var instructions = FindObjectOfType<InstructionCanvasController>(true);
            if (instructions != null)
            {
                Suspend(instructions);
                Hide(instructions.m_InstructionCanvas);
                Hide(instructions.m_MethodInstruction);
                Hide(instructions.m_PracticeOrNot);
            }

            var session = FindObjectOfType<TypingTestSession>(true);
            if (session != null && session.m_ResultPanel != null)
                Hide(session.m_ResultPanel.gameObject);
        }

        void RestoreProductionState()
        {
            for (var i = m_HiddenObjects.Count - 1; i >= 0; i--)
            {
                var state = m_HiddenObjects[i];
                if (state.gameObject != null)
                    state.gameObject.SetActive(state.wasActive);
            }
            m_HiddenObjects.Clear();

            for (var i = m_SuspendedBehaviours.Count - 1; i >= 0; i--)
            {
                var state = m_SuspendedBehaviours[i];
                if (state.behaviour != null)
                    state.behaviour.enabled = state.wasEnabled;
            }
            m_SuspendedBehaviours.Clear();

            if (m_HasPreviousInputMode && m_ModeSelector != null)
                m_ModeSelector.SetInputMode(m_PreviousInputMode);
            m_HasPreviousInputMode = false;
        }

        void Suspend(Behaviour behaviour)
        {
            if (behaviour == null)
                return;
            m_SuspendedBehaviours.Add(new BehaviourState(behaviour, behaviour.enabled));
            behaviour.enabled = false;
        }

        void Hide(GameObject value)
        {
            if (value == null)
                return;
            m_HiddenObjects.Add(new GameObjectState(value, value.activeSelf));
            value.SetActive(false);
        }

        void SetSwipeMode()
        {
            m_ModeSelector = FindObjectOfType<VRKeyboardInputModeSelector>(true);
            if (m_ModeSelector == null)
                return;

            m_PreviousInputMode = m_ModeSelector.currentInputMode;
            m_HasPreviousInputMode = true;
            m_ModeSelector.SetInputMode(VRKeyboardInputMode.Swipe);
        }

        void CreateVrOverlay()
        {
            DestroyVrOverlay();

            var camera = Camera.main;
            if (camera == null)
                camera = FindObjectOfType<Camera>(true);
            if (camera == null)
                return;

            m_OverlayRoot = new GameObject("Swipe Validation Overlay");
            m_OverlayRoot.transform.SetParent(camera.transform, false);
            m_OverlayRoot.transform.localPosition = new Vector3(0f, 0.18f, 1.15f);
            m_OverlayRoot.transform.localRotation = Quaternion.identity;
            m_OverlayRoot.transform.localScale = Vector3.one * 0.0012f;

            var canvas = m_OverlayRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.sortingOrder = 5000;
            var canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(920f, 480f);

            var background = new GameObject("Background");
            background.transform.SetParent(m_OverlayRoot.transform, false);
            var backgroundRect = background.AddComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            var image = background.AddComponent<Image>();
            image.color = new Color(0.015f, 0.025f, 0.05f, 0.92f);

            var textObject = new GameObject("Text");
            textObject.transform.SetParent(m_OverlayRoot.transform, false);
            var textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(35f, 25f);
            textRect.offsetMax = new Vector2(-35f, -25f);
            m_OverlayText = textObject.AddComponent<TextMeshProUGUI>();
            m_OverlayText.fontSize = 38f;
            m_OverlayText.alignment = TextAlignmentOptions.Center;
            m_OverlayText.enableWordWrapping = true;
            m_OverlayText.color = Color.white;
            m_OverlayText.richText = true;
        }

        void DestroyVrOverlay()
        {
            if (m_OverlayRoot != null)
                Destroy(m_OverlayRoot);
            m_OverlayRoot = null;
            m_OverlayText = null;
        }

        void RefreshOverlay()
        {
            if (m_OverlayText != null)
                m_OverlayText.text = BuildOverlayText();
        }

        string BuildOverlayText()
        {
            if (!m_Active)
                return "Swipe validation is OFF — press F8, or hold both Grip + primary buttons for 2 seconds.";

            if (m_Schedule.Count == 0)
                return "Swipe validation is active, but no target words were loaded.";

            var completed = m_ScheduleIndex;
            var top1 = completed == 0 ? 0d : m_Top1Correct * 100d / completed;
            var top5 = completed == 0 ? 0d : m_Top5Correct * 100d / completed;
            var targetText = completed < m_Schedule.Count
                ? "<size=70><b>" + m_Schedule[completed].word.ToUpperInvariant() + "</b></size>"
                : "<size=58><b>SESSION COMPLETE</b></size>";

            return
                "<b>DEVELOPER SWIPE VALIDATION</b>\n" +
                targetText + "\n" +
                "Progress: " + completed + "/" + m_Schedule.Count +
                "   Top-1: " + top1.ToString("F1", CultureInfo.InvariantCulture) + "%" +
                "   Top-5: " + top5.ToString("F1", CultureInfo.InvariantCulture) + "%\n" +
                m_LastResult + "\n" +
                "All attempts are retained. F8 / both Grip + primary buttons (2 s) exits.\n" +
                "Output: " + m_SessionDirectory;
        }

        void ReportReflectionFailure(string message)
        {
            if (m_ReflectionFailureReported)
                return;
            m_ReflectionFailureReported = true;
            Debug.LogError("Swipe developer validation disabled: " + message, this);
        }

        static bool HasCommandLineFlag(string flag)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static int ReadCommandLineInt(string prefix, int fallback, int minimum, int maximum)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length; i++)
            {
                if (!arguments[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = arguments[i].Substring(prefix.Length);
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    return Mathf.Clamp(parsed, minimum, maximum);
            }
            return fallback;
        }

        static string ReadPrivateField(object instance, string fieldName)
        {
            if (instance == null)
                return string.Empty;
            var field = instance.GetType().GetField(fieldName, s_InstanceFields);
            var value = field != null ? field.GetValue(instance) : null;
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        static KeyValuePair<string, string> Pair(string name, object value)
        {
            return new KeyValuePair<string, string>(
                name,
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        static double Percentile(IReadOnlyList<double> values, double percentile)
        {
            if (values == null || values.Count == 0)
                return 0d;

            var sorted = new List<double>(values);
            sorted.Sort();
            var position = Math.Max(0d, Math.Min(1d, percentile)) * (sorted.Count - 1);
            var lower = (int)Math.Floor(position);
            var upper = (int)Math.Ceiling(position);
            if (lower == upper)
                return sorted[lower];
            var fraction = position - lower;
            return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
        }

        static string Number(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        static string Csv(params string[] values)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    builder.Append(',');
                builder.Append('"');
                builder.Append((values[i] ?? string.Empty).Replace("\"", "\"\""));
                builder.Append('"');
            }
            return builder.ToString();
        }

        static void WriteUtf8(string path, string content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        static void AppendUtf8(string path, string content)
        {
            File.AppendAllText(path, content, new UTF8Encoding(false));
        }

        sealed class TraceSnapshot
        {
            public readonly List<GesturePoint> points = new List<GesturePoint>();
        }

        readonly struct TargetAttempt
        {
            public readonly string word;
            public readonly int repetition;

            public TargetAttempt(string word, int repetition)
            {
                this.word = word;
                this.repetition = repetition;
            }
        }

        readonly struct BehaviourState
        {
            public readonly Behaviour behaviour;
            public readonly bool wasEnabled;

            public BehaviourState(Behaviour behaviour, bool wasEnabled)
            {
                this.behaviour = behaviour;
                this.wasEnabled = wasEnabled;
            }
        }

        readonly struct GameObjectState
        {
            public readonly GameObject gameObject;
            public readonly bool wasActive;

            public GameObjectState(GameObject gameObject, bool wasActive)
            {
                this.gameObject = gameObject;
                this.wasActive = wasActive;
            }
        }
    }
}
