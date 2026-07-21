using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace VRTyping.Keyboard
{
    /// <summary>
    /// 60Hz 采样得到的单个滑动点。
    /// position 必须已经投影到二维键盘平面；time 使用 Time.time 或同一时间基准下的秒数。
    /// </summary>
    [Serializable]
    public struct GesturePoint
    {
        public Vector2 position;
        public float time;

        public GesturePoint(Vector2 position, float time)
        {
            this.position = position;
            this.time = time;
        }
    }

    /// <summary>
    /// 一个候选词及其各模型分数。分数越低表示越匹配；confidence 越高表示越可靠。
    /// </summary>
    [Serializable]
    public class SwipeCandidate
    {
        public string word;

        public float finalScore;
        public float orderedKeyScore;
        public float dtwScore;
        public float startScore;
        public float endScore;
        public float directionScore;
        public float pathPenalty;
        public float frequencyBonus;
        public float confidence;

        // 额外暴露两个商业输入法常用模型，方便调参与调试。
        public float keyProbabilityScore;
        public float speedReward;
    }

    /// <summary>
    /// 多模型融合权重。除 frequency/speed 是 bonus 外，其余都是距离或惩罚项。
    /// </summary>
    [Serializable]
    public sealed class SwipeScoreWeights
    {
        [Range(0f, 1f)] public float orderedKey = 0.30f;
        [Range(0f, 1f)] public float dtw = 0.25f;
        [Range(0f, 1f)] public float start = 0.10f;
        [Range(0f, 1f)] public float end = 0.10f;
        [Range(0f, 1f)] public float direction = 0.10f;
        [Range(0f, 1f)] public float pathPenalty = 0.05f;
        [Range(0f, 1f)] public float wordFrequency = 0.05f;
        [Range(0f, 1f)] public float speedReward = 0.05f;
        [Range(0f, 1f)] public float keyProbability = 0.10f;
    }

    /// <summary>
    /// 完整的 VR Swipe Typing 识别器。
    /// 输入整条轨迹，输出 Top-K 候选；不会把轨迹硬转换成“经过了哪些键”再查词典。
    /// </summary>
    public sealed class SwipeTypingRecognizer : MonoBehaviour
    {
        const int AlphabetSize = 26;
        const float Epsilon = 0.000001f;

        [Header("Vocabulary")]
        public TextAsset m_WordListAsset;
        public int m_MaxWords = 20000;

        [Header("Trajectory Preprocessing")]
        public int m_ResampleCount = 64;
        public float m_MinDistance = 0.004f;
        public int m_MovingAverageRadius = 2;
        public float m_OutlierSigma = 3.0f;
        public bool m_NormalizeRotation;

        [Header("Keyboard Model")]
        public float m_KeyRadius = 0.095f;
        public float m_KeyProbabilitySigma = 0.075f;

        [Header("Candidate Generation")]
        public int m_DefaultTopK = 5;
        public int m_MaxCandidateTemplates = 2000;
        [Min(16)] public int m_MaxFullyScoredCandidates = 96;
        public int m_SoftEndpointLetterCount = 3;
        public float m_StartMismatchPenalty = 0.18f;
        public float m_EndMismatchPenalty = 0.18f;

        [Header("Scoring")]
        public SwipeScoreWeights m_Weights = new SwipeScoreWeights();
        public int m_DtwWindowRadius = 10;
        public float m_ConfidenceScale = 0.20f;
        public float m_ConfidenceScoreWeight = 0.85f;
        public float m_ConfidenceSpeedWeight = 0.15f;
        public float m_MinAutoCommitConfidence = 0.45f;

        readonly List<GesturePoint> m_CurrentGesture = new List<GesturePoint>(128);
        readonly List<WordTemplate> m_Templates = new List<WordTemplate>(5000);
        readonly Dictionary<char, Vector2> m_KeyPositions = new Dictionary<char, Vector2>(AlphabetSize);
        readonly Dictionary<char, List<WordTemplate>> m_ByStart = new Dictionary<char, List<WordTemplate>>(AlphabetSize);
        readonly Dictionary<char, List<WordTemplate>> m_ByEnd = new Dictionary<char, List<WordTemplate>>(AlphabetSize);
        readonly Dictionary<int, List<WordTemplate>> m_ByStartEnd = new Dictionary<int, List<WordTemplate>>(AlphabetSize * AlphabetSize);
        readonly List<CandidateSeed> m_CandidateScratch = new List<CandidateSeed>(1024);
        readonly List<CoarseCandidateSeed> m_CoarseCandidateScratch = new List<CoarseCandidateSeed>(1024);
        readonly HashSet<string> m_CandidateDedup = new HashSet<string>();

        bool m_DatabaseDirty = true;

        public IReadOnlyDictionary<char, Vector2> keyPositions => m_KeyPositions;
        public IReadOnlyList<WordTemplate> templates => m_Templates;
        public float minAutoCommitConfidence => m_MinAutoCommitConfidence;

        void Awake()
        {
            EnsureInitialized();
        }

        void OnValidate()
        {
            m_ResampleCount = Mathf.Max(2, m_ResampleCount);
            m_MaxWords = Mathf.Max(1, m_MaxWords);
            m_MaxFullyScoredCandidates = Mathf.Max(16, m_MaxFullyScoredCandidates);
            m_DatabaseDirty = true;
        }

        /// <summary>
        /// 使用已有 SwipeKeyboardLayout 初始化 26 个字母键中心。
        /// </summary>
        public void SetKeyboardLayout(SwipeKeyboardLayout layout)
        {
            if (layout == null)
                return;

            SetKeyboardLayout(layout.keyPositions);
        }

        /// <summary>
        /// 使用任意二维键盘坐标初始化字母键中心。
        /// 坐标建议是归一化键盘坐标，用户轨迹和模板必须在同一坐标系。
        /// </summary>
        public void SetKeyboardLayout(IReadOnlyDictionary<char, Vector2> keyPositions)
        {
            if (keyPositions == null)
                return;

            m_KeyPositions.Clear();
            foreach (var pair in keyPositions)
            {
                var letter = char.ToLowerInvariant(pair.Key);
                if (letter < 'a' || letter > 'z')
                    continue;

                m_KeyPositions[letter] = pair.Value;
            }

            m_DatabaseDirty = true;
        }

        /// <summary>
        /// 从代码传入词典。每一项可以是 "word" 或 "word frequency"。
        /// </summary>
        public void SetVocabulary(IEnumerable<string> entries)
        {
            BuildTemplates(ParseVocabulary(entries, m_MaxWords));
        }

        /// <summary>
        /// 开始一次滑动输入。
        /// </summary>
        public void BeginGesture()
        {
            m_CurrentGesture.Clear();
        }

        /// <summary>
        /// 以约 60Hz 调用，记录当前手指/射线在键盘二维平面上的位置。
        /// </summary>
        public void SampleGesturePoint(Vector2 keyboardPlanePosition)
        {
            SampleGesturePoint(keyboardPlanePosition, Time.time);
        }

        public void SampleGesturePoint(Vector2 keyboardPlanePosition, float time)
        {
            if (m_CurrentGesture.Count > 0)
            {
                var last = m_CurrentGesture[m_CurrentGesture.Count - 1];
                if (Vector2.Distance(last.position, keyboardPlanePosition) < m_MinDistance &&
                    Mathf.Abs(last.time - time) < 0.010f)
                {
                    return;
                }
            }

            m_CurrentGesture.Add(new GesturePoint(keyboardPlanePosition, time));
        }

        /// <summary>
        /// 结束当前滑动并返回 Top-K 候选。
        /// </summary>
        public List<SwipeCandidate> EndGesture()
        {
            return EndGesture(m_DefaultTopK);
        }

        public List<SwipeCandidate> EndGesture(int topK)
        {
            var result = Recognize(m_CurrentGesture, topK);
            m_CurrentGesture.Clear();
            return result;
        }

        /// <summary>
        /// 从整条轨迹直接识别单词。输入点越原始越好，本函数内部会完成清洗、平滑、归一化和重采样。
        /// </summary>
        public List<SwipeCandidate> Recognize(IList<GesturePoint> rawGesture, int topK = 5)
        {
            EnsureInitialized();

            topK = Mathf.Max(1, topK);
            var results = new List<SwipeCandidate>(topK);
            if (rawGesture == null || rawGesture.Count < 2 || m_Templates.Count == 0 || m_KeyPositions.Count == 0)
                return results;

            var processed = PreprocessTrajectory(rawGesture);
            if (processed.cleaned.Count < 2)
                return results;

            var startLetters = GetNearestLetters(processed.cleaned[0].position, m_SoftEndpointLetterCount);
            var endLetters = GetNearestLetters(processed.cleaned[processed.cleaned.Count - 1].position, m_SoftEndpointLetterCount);
            var seeds = GenerateCandidates(startLetters, endLetters);
            if (seeds.Count == 0)
                return results;

            BuildCoarseCandidateShortlist(seeds, processed, Mathf.Max(topK, m_MaxFullyScoredCandidates));
            var probabilities = BuildKeyProbabilities(processed.cleaned);
            for (var i = 0; i < m_CoarseCandidateScratch.Count; i++)
            {
                var seed = m_CoarseCandidateScratch[i].seed;
                var template = seed.template;
                var orderedMatch = ScoreOrderedKeySequenceInternal(processed.cleaned, template.keySequence);

                var candidate = new SwipeCandidate();
                candidate.word = template.word;
                candidate.orderedKeyScore = orderedMatch.averageDistance + seed.endpointPenalty;
                candidate.keyProbabilityScore = ScoreKeyProbabilitySequence(probabilities, template.keySequence);
                candidate.dtwScore = ScoreDTW(processed.normalizedShape, template.normalizedShapePoints);
                candidate.startScore = Vector2.Distance(processed.cleaned[0].position, template.keyPoints[0]) +
                                       (seed.startMatched ? 0f : m_StartMismatchPenalty);
                candidate.endScore = Vector2.Distance(processed.cleaned[processed.cleaned.Count - 1].position,
                                         template.keyPoints[template.keyPoints.Length - 1]) +
                                     (seed.endMatched ? 0f : m_EndMismatchPenalty);
                candidate.directionScore = ScoreDirectionSimilarity(processed.resampledLocations, template.locationPoints);
                candidate.pathPenalty = ScorePathLengthPenalty(processed.pathLength, template.pathLength);
                candidate.frequencyBonus = template.frequencyScore;
                candidate.speedReward = ScoreSpeedReward(processed.cleaned, orderedMatch.matchedIndices, template.keySequence);

                candidate.finalScore =
                    m_Weights.orderedKey * candidate.orderedKeyScore +
                    m_Weights.keyProbability * candidate.keyProbabilityScore +
                    m_Weights.dtw * candidate.dtwScore +
                    m_Weights.start * candidate.startScore +
                    m_Weights.end * candidate.endScore +
                    m_Weights.direction * candidate.directionScore +
                    m_Weights.pathPenalty * candidate.pathPenalty -
                    m_Weights.wordFrequency * candidate.frequencyBonus -
                    m_Weights.speedReward * candidate.speedReward;

                InsertCandidate(results, candidate, topK);
            }

            ApplyConfidence(results);
            return results;
        }

        void BuildCoarseCandidateShortlist(
            IReadOnlyList<CandidateSeed> seeds,
            ProcessedTrajectory processed,
            int capacity)
        {
            m_CoarseCandidateScratch.Clear();
            if (seeds == null || processed == null)
                return;

            capacity = Mathf.Max(1, capacity);
            for (var i = 0; i < seeds.Count; i++)
            {
                var seed = seeds[i];
                var template = seed.template;
                if (template == null)
                    continue;

                // Cheap, allocation-free approximation. Full ordered-key DP, DTW,
                // probability and speed scoring only run for the best shortlist.
                var locationDistance = SwipeTrajectoryUtility.MeanDistance(
                    processed.resampledLocations,
                    template.locationPoints);
                var shapeDistance = SwipeTrajectoryUtility.MeanDistance(
                    processed.normalizedShape,
                    template.normalizedShapePoints);
                var pathPenalty = ScorePathLengthPenalty(processed.pathLength, template.pathLength);
                var score = locationDistance * 0.45f +
                            shapeDistance * 0.35f +
                            seed.endpointPenalty * 0.15f +
                            pathPenalty * 0.05f -
                            template.frequencyScore * 0.02f;

                m_CoarseCandidateScratch.Add(new CoarseCandidateSeed(seed, score));
            }

            m_CoarseCandidateScratch.Sort(CompareCoarseCandidates);
            if (m_CoarseCandidateScratch.Count > capacity)
            {
                m_CoarseCandidateScratch.RemoveRange(
                    capacity,
                    m_CoarseCandidateScratch.Count - capacity);
            }
        }

        static int CompareCoarseCandidates(CoarseCandidateSeed a, CoarseCandidateSeed b)
        {
            return a.score.CompareTo(b.score);
        }

        /// <summary>
        /// 完整预处理：去重复、平滑、去孤立异常点、保留键盘坐标重采样，并生成形状归一化轨迹。
        /// </summary>
        public ProcessedTrajectory PreprocessTrajectory(IList<GesturePoint> rawGesture)
        {
            var cleaned = RemoveDuplicatePoints(rawGesture, m_MinDistance);
            cleaned = MovingAverage(cleaned, m_MovingAverageRadius);
            cleaned = RemoveOutliers(cleaned, m_OutlierSigma);

            var locationPoints = ExtractPositions(cleaned);
            var resampledLocations = SwipeTrajectoryUtility.Resample(locationPoints, m_ResampleCount);
            var normalizedShape = NormalizeScaleAndTranslation(resampledLocations, m_NormalizeRotation);
            var pathLength = SwipeTrajectoryUtility.PathLength(locationPoints);

            return new ProcessedTrajectory(cleaned, resampledLocations, normalizedShape, pathLength);
        }

        public float ScoreOrderedKeySequence(IList<GesturePoint> points, IReadOnlyList<char> targetKeys)
        {
            return ScoreOrderedKeySequenceInternal(points, targetKeys).averageDistance;
        }

        public float ScoreDTW(IList<Vector2> userPoints, IList<Vector2> templatePoints)
        {
            return SwipeTrajectoryUtility.DynamicTimeWarpingDistance(userPoints, templatePoints, m_DtwWindowRadius);
        }

        public Dictionary<char, float> GetKeyProbability(Vector2 point)
        {
            EnsureInitialized();
            return CalculateKeyProbability(point);
        }

        void EnsureInitialized()
        {
            if (m_KeyPositions.Count == 0)
                BuildDefaultQwertyLayout();

            if (!m_DatabaseDirty && m_Templates.Count > 0)
                return;

            var vocabulary = m_WordListAsset != null
                ? ParseVocabulary(m_WordListAsset.text, m_MaxWords)
                : ParseVocabulary(DefaultVocabulary(), m_MaxWords);
            BuildTemplates(vocabulary);
        }

        void BuildDefaultQwertyLayout()
        {
            m_KeyPositions.Clear();

            AddKeyboardRow("qwertyuiop", 0.00f, 1.00f, 10);
            AddKeyboardRow("asdfghjkl", 0.50f, 0.50f, 10);
            AddKeyboardRow("zxcvbnm", 1.50f, 0.00f, 10);

            void AddKeyboardRow(string row, float xOffset, float y, float fullWidth)
            {
                for (var i = 0; i < row.Length; i++)
                {
                    var x = (xOffset + i) / (fullWidth - 1f);
                    m_KeyPositions[row[i]] = new Vector2(x, y);
                }
            }
        }

        void BuildTemplates(IReadOnlyList<VocabularyEntry> vocabulary)
        {
            m_Templates.Clear();
            m_ByStart.Clear();
            m_ByEnd.Clear();
            m_ByStartEnd.Clear();

            if (vocabulary == null)
            {
                m_DatabaseDirty = false;
                return;
            }

            for (var i = 0; i < vocabulary.Count; i++)
            {
                var entry = vocabulary[i];
                if (!TryCreateTemplate(entry.word, entry.frequencyScore, out var template))
                    continue;

                m_Templates.Add(template);
                AddToIndex(m_ByStart, template.startLetter, template);
                AddToIndex(m_ByEnd, template.endLetter, template);
                AddToStartEndIndex(template.startLetter, template.endLetter, template);
            }

            m_DatabaseDirty = false;
        }

        bool TryCreateTemplate(string word, float frequencyScore, out WordTemplate template)
        {
            template = null;
            var normalizedWord = NormalizeWord(word);
            if (string.IsNullOrEmpty(normalizedWord))
                return false;

            var collapsed = CollapseRepeatedLetters(normalizedWord);
            if (collapsed.Length == 0)
                return false;

            var keyPoints = new Vector2[collapsed.Length];
            var keySequence = new char[collapsed.Length];
            for (var i = 0; i < collapsed.Length; i++)
            {
                var letter = collapsed[i];
                if (!m_KeyPositions.TryGetValue(letter, out keyPoints[i]))
                    return false;
                keySequence[i] = letter;
            }

            var pathSource = BuildTemplatePolyline(keyPoints);
            var locationPoints = SwipeTrajectoryUtility.Resample(pathSource, m_ResampleCount);
            var normalizedShapePoints = NormalizeScaleAndTranslation(locationPoints, m_NormalizeRotation);
            var pathLength = SwipeTrajectoryUtility.PathLength(pathSource);

            template = new WordTemplate(
                normalizedWord,
                collapsed,
                frequencyScore,
                keySequence,
                keyPoints,
                locationPoints,
                normalizedShapePoints,
                pathLength);
            return true;
        }

        static Vector2[] BuildTemplatePolyline(IReadOnlyList<Vector2> keyPoints)
        {
            if (keyPoints.Count > 1)
            {
                var points = new Vector2[keyPoints.Count];
                for (var i = 0; i < keyPoints.Count; i++)
                    points[i] = keyPoints[i];
                return points;
            }

            // 单字母词没有路径长度；复制一个极近点，让重采样和 DTW 保持稳定。
            var single = keyPoints[0];
            return new[] { single, single + Vector2.right * 0.0001f };
        }

        List<CandidateSeed> GenerateCandidates(IReadOnlyList<LetterDistance> startLetters, IReadOnlyList<LetterDistance> endLetters)
        {
            m_CandidateScratch.Clear();
            m_CandidateDedup.Clear();

            for (var i = 0; i < startLetters.Count; i++)
            {
                for (var j = 0; j < endLetters.Count; j++)
                {
                    var start = startLetters[i].letter;
                    var end = endLetters[j].letter;
                    var penalty = startLetters[i].distance * 0.35f + endLetters[j].distance * 0.35f;
                    AddCandidateBucket(GetStartEndBucket(start, end), true, true, penalty);
                }
            }

            for (var i = 0; i < startLetters.Count; i++)
            {
                var start = startLetters[i].letter;
                AddCandidateBucket(GetBucket(m_ByStart, start), true, false, startLetters[i].distance + m_EndMismatchPenalty);
            }

            for (var i = 0; i < endLetters.Count; i++)
            {
                var end = endLetters[i].letter;
                AddCandidateBucket(GetBucket(m_ByEnd, end), false, true, endLetters[i].distance + m_StartMismatchPenalty);
            }

            // 如果首尾候选太少，按廉价端点距离补充一部分词。这里仍只是候选生成，不做完整模型评分。
            if (m_CandidateScratch.Count < Mathf.Min(64, m_MaxCandidateTemplates))
                AddCheapFallbackCandidates(startLetters[0].letter, endLetters[0].letter);

            return m_CandidateScratch;
        }

        void AddCandidateBucket(IReadOnlyList<WordTemplate> bucket, bool startMatched, bool endMatched, float endpointPenalty)
        {
            if (bucket == null)
                return;

            for (var i = 0; i < bucket.Count && m_CandidateScratch.Count < m_MaxCandidateTemplates; i++)
                AddCandidate(bucket[i], startMatched, endMatched, endpointPenalty);
        }

        void AddCheapFallbackCandidates(char bestStart, char bestEnd)
        {
            for (var i = 0; i < m_Templates.Count && m_CandidateScratch.Count < m_MaxCandidateTemplates; i++)
            {
                var template = m_Templates[i];
                var startMatched = template.startLetter == bestStart;
                var endMatched = template.endLetter == bestEnd;
                var penalty = (startMatched ? 0f : m_StartMismatchPenalty) + (endMatched ? 0f : m_EndMismatchPenalty);
                AddCandidate(template, startMatched, endMatched, penalty);
            }
        }

        void AddCandidate(WordTemplate template, bool startMatched, bool endMatched, float endpointPenalty)
        {
            if (template == null || !m_CandidateDedup.Add(template.word))
                return;

            m_CandidateScratch.Add(new CandidateSeed(template, startMatched, endMatched, endpointPenalty));
        }

        OrderedMatch ScoreOrderedKeySequenceInternal(IList<GesturePoint> points, IReadOnlyList<char> targetKeys)
        {
            var result = new OrderedMatch(targetKeys.Count);
            if (points == null || points.Count == 0 || targetKeys == null || targetKeys.Count == 0)
            {
                result.averageDistance = float.PositiveInfinity;
                return result;
            }

            var pointCount = points.Count;
            var keyCount = targetKeys.Count;
            var costs = new float[pointCount + 1, keyCount + 1];
            var took = new bool[pointCount + 1, keyCount + 1];
            const float skipPointCost = 0.0005f;

            for (var j = 1; j <= keyCount; j++)
                costs[0, j] = float.PositiveInfinity;

            for (var i = 1; i <= pointCount; i++)
            {
                costs[i, 0] = 0f;
                for (var j = 1; j <= keyCount; j++)
                {
                    var skipCost = costs[i - 1, j] + skipPointCost;
                    var takeCost = costs[i - 1, j - 1] + GetOrderedKeyLocalCost(points[i - 1].position, targetKeys[j - 1]);
                    if (takeCost <= skipCost)
                    {
                        costs[i, j] = takeCost;
                        took[i, j] = true;
                    }
                    else
                    {
                        costs[i, j] = skipCost;
                    }
                }
            }

            var pointIndex = pointCount;
            var keyIndex = keyCount;
            while (pointIndex > 0 && keyIndex > 0)
            {
                if (took[pointIndex, keyIndex])
                {
                    result.matchedIndices[keyIndex - 1] = pointIndex - 1;
                    pointIndex--;
                    keyIndex--;
                }
                else
                {
                    pointIndex--;
                }
            }

            while (keyIndex > 0)
            {
                result.matchedIndices[keyIndex - 1] = 0;
                keyIndex--;
            }

            result.averageDistance = costs[pointCount, keyCount] / Mathf.Max(1, keyCount);
            return result;
        }

        float GetOrderedKeyLocalCost(Vector2 point, char key)
        {
            if (!m_KeyPositions.TryGetValue(key, out var keyCenter))
                return 1f;

            var distance = Vector2.Distance(point, keyCenter);
            if (distance <= m_KeyRadius)
                return distance;

            var miss = (distance - m_KeyRadius) / Mathf.Max(m_KeyRadius, Epsilon);
            return distance + miss * 0.12f;
        }

        List<Dictionary<char, float>> BuildKeyProbabilities(IList<GesturePoint> points)
        {
            var result = new List<Dictionary<char, float>>(points.Count);
            for (var i = 0; i < points.Count; i++)
                result.Add(CalculateKeyProbability(points[i].position));
            return result;
        }

        Dictionary<char, float> CalculateKeyProbability(Vector2 point)
        {
            var result = new Dictionary<char, float>(AlphabetSize);
            var sigma2 = m_KeyProbabilitySigma * m_KeyProbabilitySigma;
            var sum = 0f;

            foreach (var pair in m_KeyPositions)
            {
                var sqrDistance = (point - pair.Value).sqrMagnitude;
                var weight = Mathf.Exp(-sqrDistance / (2f * sigma2));
                result[pair.Key] = weight;
                sum += weight;
            }

            if (sum <= Epsilon)
                return result;

            var letters = new List<char>(result.Keys);
            for (var i = 0; i < letters.Count; i++)
                result[letters[i]] /= sum;

            return result;
        }

        float ScoreKeyProbabilitySequence(IReadOnlyList<Dictionary<char, float>> probabilities, IReadOnlyList<char> targetKeys)
        {
            if (probabilities == null || targetKeys == null || probabilities.Count == 0 || targetKeys.Count == 0)
                return 1f;

            // 单调 DP：允许跳过轨迹点，但目标字母必须按顺序匹配到概率较高的位置。
            var pointCount = probabilities.Count;
            var keyCount = targetKeys.Count;
            var previous = new float[keyCount + 1];
            var current = new float[keyCount + 1];

            for (var j = 1; j <= keyCount; j++)
                previous[j] = 1000f;

            for (var i = 1; i <= pointCount; i++)
            {
                current[0] = 0f;
                for (var j = 1; j <= keyCount; j++)
                {
                    var skipPoint = previous[j] + 0.010f;
                    var probability = probabilities[i - 1].TryGetValue(targetKeys[j - 1], out var p)
                        ? Mathf.Max(p, 0.0001f)
                        : 0.0001f;
                    var takePoint = previous[j - 1] + -Mathf.Log(probability);
                    current[j] = Mathf.Min(skipPoint, takePoint);
                }

                var swap = previous;
                previous = current;
                current = swap;
            }

            return Mathf.Clamp01(previous[keyCount] / (keyCount * 6f));
        }

        float ScoreDirectionSimilarity(IList<Vector2> userPoints, IList<Vector2> templatePoints)
        {
            if (userPoints == null || templatePoints == null || userPoints.Count < 2 || userPoints.Count != templatePoints.Count)
                return 1f;

            var total = 0f;
            var compared = 0;
            for (var i = 1; i < userPoints.Count; i++)
            {
                var userVector = userPoints[i] - userPoints[i - 1];
                var templateVector = templatePoints[i] - templatePoints[i - 1];
                if (userVector.sqrMagnitude < Epsilon || templateVector.sqrMagnitude < Epsilon)
                    continue;

                var angle = Vector2.Angle(userVector, templateVector);
                total += angle / 180f;
                compared++;
            }

            return compared == 0 ? 1f : total / compared;
        }

        float ScorePathLengthPenalty(float userLength, float templateLength)
        {
            if (templateLength <= Epsilon)
                return userLength;

            var ratio = userLength / templateLength;
            if (ratio <= 1f)
                return Mathf.Abs(1f - ratio) * 0.20f;

            // 乱划越长，惩罚越明显；用 log 避免极端长轨迹把总分完全冲爆。
            return Mathf.Log(ratio) + Mathf.Max(0f, ratio - 1.7f) * 0.20f;
        }

        float ScoreSpeedReward(IList<GesturePoint> points, IReadOnlyList<int> matchedIndices, IReadOnlyList<char> targetKeys)
        {
            if (points == null || matchedIndices == null || points.Count < 3 || matchedIndices.Count == 0)
                return 0f;

            var speeds = CalculatePointSpeeds(points);
            var medianSpeed = Median(speeds);
            if (medianSpeed <= Epsilon)
                return 0f;

            var reward = 0f;
            var count = 0;
            for (var i = 0; i < matchedIndices.Count; i++)
            {
                var index = Mathf.Clamp(matchedIndices[i], 0, speeds.Length - 1);
                if (!m_KeyPositions.TryGetValue(targetKeys[i], out var keyCenter))
                    continue;

                var distance = Vector2.Distance(points[index].position, keyCenter);
                var nearKey = Mathf.Clamp01(1f - distance / Mathf.Max(m_KeyRadius, Epsilon));
                var slowDown = Mathf.Clamp01(1f - speeds[index] / medianSpeed);
                reward += nearKey * slowDown;
                count++;
            }

            return count == 0 ? 0f : reward / count;
        }

        static float[] CalculatePointSpeeds(IList<GesturePoint> points)
        {
            var speeds = new float[points.Count];
            for (var i = 1; i < points.Count; i++)
            {
                var dt = Mathf.Max(points[i].time - points[i - 1].time, Epsilon);
                speeds[i] = Vector2.Distance(points[i].position, points[i - 1].position) / dt;
            }

            if (speeds.Length > 1)
                speeds[0] = speeds[1];
            return speeds;
        }

        static List<GesturePoint> RemoveDuplicatePoints(IList<GesturePoint> rawGesture, float minDistance)
        {
            var result = new List<GesturePoint>(rawGesture != null ? rawGesture.Count : 0);
            if (rawGesture == null)
                return result;

            for (var i = 0; i < rawGesture.Count; i++)
            {
                var point = rawGesture[i];
                if (result.Count > 0 &&
                    Vector2.Distance(result[result.Count - 1].position, point.position) < minDistance)
                {
                    continue;
                }

                result.Add(point);
            }

            return result;
        }

        static List<GesturePoint> MovingAverage(IList<GesturePoint> points, int radius)
        {
            if (points == null || points.Count <= 2 || radius <= 0)
                return points == null ? new List<GesturePoint>() : new List<GesturePoint>(points);

            var result = new List<GesturePoint>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                var from = Mathf.Max(0, i - radius);
                var to = Mathf.Min(points.Count - 1, i + radius);
                var sum = Vector2.zero;
                var count = 0;
                for (var j = from; j <= to; j++)
                {
                    sum += points[j].position;
                    count++;
                }

                result.Add(new GesturePoint(sum / count, points[i].time));
            }

            return result;
        }

        static List<GesturePoint> RemoveOutliers(IList<GesturePoint> points, float sigma)
        {
            if (points == null || points.Count < 4)
                return points == null ? new List<GesturePoint>() : new List<GesturePoint>(points);

            var steps = new float[points.Count - 1];
            var mean = 0f;
            for (var i = 1; i < points.Count; i++)
            {
                var step = Vector2.Distance(points[i - 1].position, points[i].position);
                steps[i - 1] = step;
                mean += step;
            }
            mean /= steps.Length;

            var variance = 0f;
            for (var i = 0; i < steps.Length; i++)
            {
                var delta = steps[i] - mean;
                variance += delta * delta;
            }

            var std = Mathf.Sqrt(variance / steps.Length);
            var threshold = mean + std * Mathf.Max(1f, sigma);
            var result = new List<GesturePoint>(points.Count) { points[0] };

            for (var i = 1; i < points.Count - 1; i++)
            {
                var previous = points[i - 1].position;
                var current = points[i].position;
                var next = points[i + 1].position;
                var isolatedSpike =
                    Vector2.Distance(previous, current) > threshold &&
                    Vector2.Distance(current, next) > threshold &&
                    Vector2.Distance(previous, next) < threshold;

                if (!isolatedSpike)
                    result.Add(points[i]);
            }

            result.Add(points[points.Count - 1]);
            return result;
        }

        static Vector2[] ExtractPositions(IList<GesturePoint> points)
        {
            var result = new Vector2[points.Count];
            for (var i = 0; i < points.Count; i++)
                result[i] = points[i].position;
            return result;
        }

        static Vector2[] NormalizeScaleAndTranslation(IList<Vector2> points, bool normalizeRotation)
        {
            var normalized = SwipeTrajectoryUtility.NormalizeShape(points);
            if (!normalizeRotation || normalized.Length < 2)
                return normalized;

            var first = normalized[0];
            var last = normalized[normalized.Length - 1];
            var direction = last - first;
            if (direction.sqrMagnitude <= Epsilon)
                return normalized;

            var angle = Mathf.Atan2(direction.y, direction.x);
            var cos = Mathf.Cos(-angle);
            var sin = Mathf.Sin(-angle);
            for (var i = 0; i < normalized.Length; i++)
            {
                var point = normalized[i];
                normalized[i] = new Vector2(point.x * cos - point.y * sin, point.x * sin + point.y * cos);
            }

            return normalized;
        }

        List<LetterDistance> GetNearestLetters(Vector2 point, int count)
        {
            count = Mathf.Clamp(count, 1, AlphabetSize);
            var letters = new List<LetterDistance>(AlphabetSize);
            foreach (var pair in m_KeyPositions)
                letters.Add(new LetterDistance(pair.Key, Vector2.Distance(point, pair.Value)));

            letters.Sort((a, b) => a.distance.CompareTo(b.distance));
            if (letters.Count > count)
                letters.RemoveRange(count, letters.Count - count);
            return letters;
        }

        static void InsertCandidate(List<SwipeCandidate> results, SwipeCandidate candidate, int capacity)
        {
            var index = 0;
            while (index < results.Count && results[index].finalScore <= candidate.finalScore)
                index++;

            if (index >= capacity)
                return;

            results.Insert(index, candidate);
            if (results.Count > capacity)
                results.RemoveAt(results.Count - 1);
        }

        void ApplyConfidence(IList<SwipeCandidate> results)
        {
            if (results == null || results.Count == 0)
                return;

            if (results.Count == 1)
            {
                results[0].confidence = BlendConfidenceWithSpeed(1f, results[0].speedReward);
                return;
            }

            var top1 = results[0].finalScore;
            var top2 = results[1].finalScore;
            var gap = Mathf.Max(0f, top2 - top1);
            var baseConfidence = 1f - Mathf.Exp(-gap / Mathf.Max(m_ConfidenceScale, Epsilon));

            for (var i = 0; i < results.Count; i++)
            {
                var rankedConfidence = i == 0 ? baseConfidence : Mathf.Max(0f, baseConfidence - 0.15f * i);
                results[i].confidence = BlendConfidenceWithSpeed(rankedConfidence, results[i].speedReward);
            }
        }

        float BlendConfidenceWithSpeed(float baseConfidence, float speedQuality)
        {
            // Confidence 仍主要来自 Top1/Top2 分数差；速度只做轻量校正，避免快速乱划被误判为高置信度。
            var scoreWeight = Mathf.Max(0f, m_ConfidenceScoreWeight);
            var speedWeight = Mathf.Max(0f, m_ConfidenceSpeedWeight);
            var total = Mathf.Max(scoreWeight + speedWeight, Epsilon);
            scoreWeight /= total;
            speedWeight /= total;

            return Mathf.Clamp01(
                Mathf.Clamp01(baseConfidence) * scoreWeight +
                Mathf.Clamp01(speedQuality) * speedWeight);
        }

        static IReadOnlyList<VocabularyEntry> ParseVocabulary(string text, int maxWords)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<VocabularyEntry>();

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return ParseVocabulary(lines, maxWords);
        }

        static IReadOnlyList<VocabularyEntry> ParseVocabulary(IEnumerable<string> entries, int maxWords)
        {
            var result = new List<VocabularyEntry>();
            if (entries == null)
                return result;

            var rank = 0;
            var seenWords = new HashSet<string>();
            foreach (var rawEntry in entries)
            {
                if (result.Count >= maxWords)
                    break;

                if (string.IsNullOrWhiteSpace(rawEntry))
                    continue;

                var tokens = rawEntry.Split(new[] { ',', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                    continue;

                var word = NormalizeWord(tokens[0]);
                if (word.Length < 2)
                    continue;

                if (!seenWords.Add(word))
                    continue;

                var frequency = 0f;
                if (tokens.Length > 1 &&
                    float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    frequency = Mathf.Log10(Mathf.Max(1f, parsed));
                }
                else
                {
                    // 没有显式词频时，把词表顺序当作 rank：越靠前越常见。
                    frequency = 1f - rank / Mathf.Max(1f, maxWords - 1f);
                }

                result.Add(new VocabularyEntry(word, frequency));
                rank++;
            }

            NormalizeFrequencyScores(result);
            return result;
        }

        static void NormalizeFrequencyScores(IList<VocabularyEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            var max = 0f;
            for (var i = 0; i < entries.Count; i++)
                max = Mathf.Max(max, entries[i].frequencyScore);

            if (max <= Epsilon)
                return;

            for (var i = 0; i < entries.Count; i++)
                entries[i] = new VocabularyEntry(entries[i].word, Mathf.Clamp01(entries[i].frequencyScore / max));
        }

        static string NormalizeWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            var builder = new StringBuilder(word.Length);
            for (var i = 0; i < word.Length; i++)
            {
                var letter = char.ToLowerInvariant(word[i]);
                if (letter >= 'a' && letter <= 'z')
                    builder.Append(letter);
            }

            return builder.ToString();
        }

        static string CollapseRepeatedLetters(string word)
        {
            if (string.IsNullOrEmpty(word))
                return string.Empty;

            var builder = new StringBuilder(word.Length);
            var previous = '\0';
            for (var i = 0; i < word.Length; i++)
            {
                var letter = word[i];
                if (letter == previous)
                    continue;

                builder.Append(letter);
                previous = letter;
            }

            return builder.ToString();
        }

        static float Median(float[] values)
        {
            if (values == null || values.Length == 0)
                return 0f;

            var copy = new float[values.Length];
            Array.Copy(values, copy, values.Length);
            Array.Sort(copy);
            var middle = copy.Length / 2;
            return copy.Length % 2 == 1
                ? copy[middle]
                : (copy[middle - 1] + copy[middle]) * 0.5f;
        }

        static void AddToIndex(Dictionary<char, List<WordTemplate>> index, char letter, WordTemplate template)
        {
            if (!index.TryGetValue(letter, out var bucket))
            {
                bucket = new List<WordTemplate>();
                index.Add(letter, bucket);
            }

            bucket.Add(template);
        }

        void AddToStartEndIndex(char start, char end, WordTemplate template)
        {
            var key = MakeStartEndKey(start, end);
            if (!m_ByStartEnd.TryGetValue(key, out var bucket))
            {
                bucket = new List<WordTemplate>();
                m_ByStartEnd.Add(key, bucket);
            }

            bucket.Add(template);
        }

        static IReadOnlyList<WordTemplate> GetBucket(Dictionary<char, List<WordTemplate>> index, char letter)
        {
            return index.TryGetValue(letter, out var bucket) ? bucket : null;
        }

        IReadOnlyList<WordTemplate> GetStartEndBucket(char start, char end)
        {
            return m_ByStartEnd.TryGetValue(MakeStartEndKey(start, end), out var bucket) ? bucket : null;
        }

        static int MakeStartEndKey(char start, char end)
        {
            return (start - 'a') * AlphabetSize + (end - 'a');
        }

        static IEnumerable<string> DefaultVocabulary()
        {
            // 兜底词表只用于没有指定 TextAsset 时保持系统可运行；正式项目请传入 1000-5000 高频英文词。
            return new[]
            {
                "the", "be", "to", "of", "and", "a", "in", "that", "have", "i",
                "it", "for", "not", "on", "with", "he", "as", "you", "do", "at",
                "this", "but", "his", "by", "from", "they", "we", "say", "her", "she",
                "or", "an", "will", "my", "one", "all", "would", "there", "their", "what",
                "so", "up", "out", "if", "about", "who", "get", "which", "go", "me",
                "when", "make", "can", "like", "time", "no", "just", "him", "know", "take",
                "people", "into", "year", "your", "good", "some", "could", "them", "see", "other",
                "than", "then", "now", "look", "only", "come", "its", "over", "think", "also",
                "back", "after", "use", "two", "how", "our", "work", "first", "well", "way",
                "even", "new", "want", "because", "any", "these", "give", "day", "most", "us",
                "apple", "apply", "about", "above", "again", "air", "area", "ask", "away", "best",
                "book", "call", "case", "child", "city", "company", "country", "course", "data", "down",
                "each", "early", "end", "example", "eye", "fact", "family", "few", "find", "game",
                "great", "group", "hand", "hello", "high", "home", "house", "important", "keep", "large",
                "last", "leave", "letter", "life", "little", "long", "man", "many", "move", "must",
                "name", "need", "never", "next", "number", "old", "open", "own", "part", "place",
                "point", "problem", "program", "public", "right", "same", "school", "seem", "small", "state",
                "still", "student", "system", "tell", "thing", "try", "turn", "under", "use", "very",
                "water", "week", "where", "while", "world", "write"
            };
        }

        readonly struct LetterDistance
        {
            public readonly char letter;
            public readonly float distance;

            public LetterDistance(char letter, float distance)
            {
                this.letter = letter;
                this.distance = distance;
            }
        }

        readonly struct CandidateSeed
        {
            public readonly WordTemplate template;
            public readonly bool startMatched;
            public readonly bool endMatched;
            public readonly float endpointPenalty;

            public CandidateSeed(WordTemplate template, bool startMatched, bool endMatched, float endpointPenalty)
            {
                this.template = template;
                this.startMatched = startMatched;
                this.endMatched = endMatched;
                this.endpointPenalty = endpointPenalty;
            }
        }

        readonly struct CoarseCandidateSeed
        {
            public readonly CandidateSeed seed;
            public readonly float score;

            public CoarseCandidateSeed(CandidateSeed seed, float score)
            {
                this.seed = seed;
                this.score = score;
            }
        }

        struct OrderedMatch
        {
            public float averageDistance;
            public int[] matchedIndices;

            public OrderedMatch(int count)
            {
                averageDistance = 0f;
                matchedIndices = new int[Mathf.Max(0, count)];
            }
        }

        readonly struct VocabularyEntry
        {
            public readonly string word;
            public readonly float frequencyScore;

            public VocabularyEntry(string word, float frequencyScore)
            {
                this.word = word;
                this.frequencyScore = frequencyScore;
            }
        }

        /// <summary>
        /// 预处理后的轨迹：cleaned 保留时间和键盘坐标；resampledLocations 用于位置/方向；
        /// normalizedShape 用于纯形状匹配。
        /// </summary>
        public sealed class ProcessedTrajectory
        {
            public readonly List<GesturePoint> cleaned;
            public readonly Vector2[] resampledLocations;
            public readonly Vector2[] normalizedShape;
            public readonly float pathLength;

            public ProcessedTrajectory(
                List<GesturePoint> cleaned,
                Vector2[] resampledLocations,
                Vector2[] normalizedShape,
                float pathLength)
            {
                this.cleaned = cleaned;
                this.resampledLocations = resampledLocations;
                this.normalizedShape = normalizedShape;
                this.pathLength = pathLength;
            }
        }

        /// <summary>
        /// 由键盘坐标自动生成的词模板，不需要人工绘制。
        /// </summary>
        public sealed class WordTemplate
        {
            public readonly string word;
            public readonly string collapsedWord;
            public readonly float frequencyScore;
            public readonly char[] keySequence;
            public readonly Vector2[] keyPoints;
            public readonly Vector2[] locationPoints;
            public readonly Vector2[] normalizedShapePoints;
            public readonly float pathLength;
            public readonly char startLetter;
            public readonly char endLetter;

            public WordTemplate(
                string word,
                string collapsedWord,
                float frequencyScore,
                char[] keySequence,
                Vector2[] keyPoints,
                Vector2[] locationPoints,
                Vector2[] normalizedShapePoints,
                float pathLength)
            {
                this.word = word;
                this.collapsedWord = collapsedWord;
                this.frequencyScore = frequencyScore;
                this.keySequence = keySequence;
                this.keyPoints = keyPoints;
                this.locationPoints = locationPoints;
                this.normalizedShapePoints = normalizedShapePoints;
                this.pathLength = pathLength;
                startLetter = keySequence[0];
                endLetter = keySequence[keySequence.Length - 1];
            }
        }
    }
}
