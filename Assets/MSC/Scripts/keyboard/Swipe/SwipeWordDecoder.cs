using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VRTyping.Keyboard
{
    // 一个候选识别结果，score 越小表示越匹配。
    public readonly struct SwipeWordCandidate
    {
        public string word { get; }
        public float score { get; }

        public SwipeWordCandidate(string word, float score)
        {
            this.word = word;
            this.score = score;
        }
    }

    // 根据用户滑动轨迹，在模板数据库里匹配最可能的单词。
    public sealed class SwipeWordDecoder
    {
        readonly SwipeTemplateDatabase m_Database;

        public SwipeWordDecoder(SwipeTemplateDatabase database)
        {
            m_Database = database;
        }

        public bool TryDecode(
            IList<Vector2> rawPoints,
            IList<string> crossedKeyIds,
            int candidateCount,
            float endpointRadius,
            int maxSequenceLengthDifference,
            int dtwWindowRadius,
            float shapeWeight,
            float locationWeight,
            float endpointWeight,
            float sequenceWeight,
            float pathLengthWeight,
            float frequencyWeight,
            out List<SwipeWordCandidate> candidates)
        {
            // 先把用户原始轨迹转换成和模板一致的采样数量、形状坐标和路径长度。
            candidates = new List<SwipeWordCandidate>(Mathf.Max(1, candidateCount));
            if (m_Database == null || rawPoints == null || rawPoints.Count < 2)
                return false;

            var locations = SwipeTrajectoryUtility.Resample(rawPoints, m_Database.sampleCount);
            var shapes = SwipeTrajectoryUtility.NormalizeShape(locations);
            var pathLength = SwipeTrajectoryUtility.PathLength(locations);
            var observed = BuildObservedSequence(crossedKeyIds);

            // 第一轮用端点和序列长度做粗过滤，速度更快，也减少明显错误候选。
            ScoreTemplates(locations, shapes, pathLength, observed, true, candidateCount,
                endpointRadius, maxSequenceLengthDifference, shapeWeight, locationWeight,
                endpointWeight, sequenceWeight, pathLengthWeight, frequencyWeight, dtwWindowRadius, candidates);

            if (candidates.Count == 0)
            {
                // 如果过滤太严格导致没有候选，就放宽条件再算一轮。
                ScoreTemplates(locations, shapes, pathLength, observed, false, candidateCount,
                    endpointRadius, maxSequenceLengthDifference, shapeWeight, locationWeight,
                    endpointWeight, sequenceWeight, pathLengthWeight, frequencyWeight, dtwWindowRadius, candidates);
            }

            return candidates.Count > 0;
        }

        void ScoreTemplates(
            Vector2[] locations,
            Vector2[] shapes,
            float pathLength,
            string observed,
            bool filterCandidates,
            int candidateCount,
            float endpointRadius,
            int maxSequenceLengthDifference,
            float shapeWeight,
            float locationWeight,
            float endpointWeight,
            float sequenceWeight,
            float pathLengthWeight,
            float frequencyWeight,
            int dtwWindowRadius,
            List<SwipeWordCandidate> candidates)
        {
            for (var i = 0; i < m_Database.templates.Count; i++)
            {
                var template = m_Database.templates[i];
                var targetWord = template.word;
                var startDistance = Vector2.Distance(locations[0], template.locationPoints[0]);
                var endDistance = Vector2.Distance(locations[locations.Length - 1],
                    template.locationPoints[template.locationPoints.Length - 1]);

                // 粗过滤：起点/终点要靠近，滑过的字母数量也不能和目标词差太多。
                if (filterCandidates &&
                    (startDistance > endpointRadius || endDistance > endpointRadius ||
                     observed.Length > 0 && Mathf.Abs(targetWord.Length - observed.Length) > maxSequenceLengthDifference))
                {
                    continue;
                }

                // 综合评分：形状、绝对位置、端点、字母序列、路径长度越接近越好；高频词减分。
                var score =
                    SwipeTrajectoryUtility.DynamicTimeWarpingDistance(shapes, template.shapePoints, dtwWindowRadius) * shapeWeight +
                    SwipeTrajectoryUtility.DynamicTimeWarpingDistance(locations, template.locationPoints, dtwWindowRadius) * locationWeight +
                    (startDistance + endDistance) * 0.5f * endpointWeight +
                    WeightedSequenceDistance(observed, targetWord) * sequenceWeight +
                    Mathf.Abs(pathLength - template.pathLength) * pathLengthWeight -
                    template.frequencyScore * frequencyWeight;

                InsertCandidate(candidates, new SwipeWordCandidate(template.word, score), candidateCount);
            }
        }

        static void InsertCandidate(List<SwipeWordCandidate> candidates, SwipeWordCandidate candidate, int capacity)
        {
            // 候选列表始终按 score 从小到大排序，并只保留前 capacity 个。
            capacity = Mathf.Max(1, capacity);
            var index = 0;
            while (index < candidates.Count && candidates[index].score <= candidate.score)
                index++;
            if (index >= capacity)
                return;

            candidates.Insert(index, candidate);
            if (candidates.Count > capacity)
                candidates.RemoveAt(candidates.Count - 1);
        }

        static string BuildObservedSequence(IList<string> keyIds)
        {
            // 从滑过的 keyId 中提取字母序列，忽略 Back/Shift 等功能键。
            if (keyIds == null)
                return string.Empty;

            var builder = new StringBuilder(keyIds.Count);
            for (var i = 0; i < keyIds.Count; i++)
            {
                var keyId = keyIds[i];
                if (string.IsNullOrEmpty(keyId) || keyId.Length != 1 || !char.IsLetter(keyId[0]))
                    continue;
                var letter = char.ToLowerInvariant(keyId[0]);
                builder.Append(letter);
            }
            return builder.ToString();
        }

        static float WeightedSequenceDistance(string observed, string target)
        {
            // 加权编辑距离：更宽容地处理漏滑字母，较严格地惩罚多出来的目标插入。
            if (string.IsNullOrEmpty(observed))
                return 0f;
            if (string.IsNullOrEmpty(target))
                return 1f;

            var previous = new float[target.Length + 1];
            var current = new float[target.Length + 1];
            for (var j = 1; j <= target.Length; j++)
                previous[j] = previous[j - 1] + 0.85f;

            for (var i = 1; i <= observed.Length; i++)
            {
                current[0] = previous[0] + 0.3f;
                for (var j = 1; j <= target.Length; j++)
                {
                    var deletion = previous[j] + 0.3f;
                    var insertion = current[j - 1] + 0.85f;
                    var substitution = previous[j - 1] + (observed[i - 1] == target[j - 1] ? 0f : 0.7f);
                    current[j] = Mathf.Min(deletion, insertion, substitution);
                }
                var swap = previous;
                previous = current;
                current = swap;
            }
            return previous[target.Length] / Mathf.Max(observed.Length, target.Length);
        }
    }
}
