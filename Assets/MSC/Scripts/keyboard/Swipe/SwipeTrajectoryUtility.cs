using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace VRTyping.Keyboard
{
    // 把 3D 键盘布局投影到 2D 归一化平面，供 Swipe 轨迹识别使用。
    public sealed class SwipeKeyboardLayout
    {
        // 键盘根节点，用来把世界坐标转换成本地坐标。
        readonly Transform m_Root;
        // 键盘平面上跨度最大的两个本地轴会被选作 2D 坐标轴。
        readonly int m_PrimaryAxis;//跨度最大的轴（长）
        readonly int m_SecondaryAxis;//跨度第二大的轴（宽）
        // 投影后的边界，用于把坐标归一化到大致 0..1 范围。
        readonly Vector2 m_Min;//键盘平面投影后的最小点（最左下角）
        readonly Vector2 m_Size;//键盘平面的长和宽
        // 每个字母键在归一化键盘平面中的位置。
        readonly Dictionary<char, Vector2> m_KeyPositions;

        public IReadOnlyDictionary<char, Vector2> keyPositions => m_KeyPositions;
        // 根据键位生成的布局签名，用于判断模板数据库是否和当前键盘布局匹配。
        public string signature { get; }

        SwipeKeyboardLayout(
            Transform root,
            int primaryAxis,
            int secondaryAxis,
            Vector2 min,
            Vector2 size,
            Dictionary<char, Vector2> keyPositions,
            string signature)
        {
            m_Root = root;
            m_PrimaryAxis = primaryAxis;
            m_SecondaryAxis = secondaryAxis;
            m_Min = min;
            m_Size = size;
            m_KeyPositions = keyPositions;
            this.signature = signature;
        }

        public Vector2 ProjectWorldPoint(Vector3 worldPoint)//把世界坐标点投影到键盘平面上，并归一化到 0..1 范围（在键盘范围里的百分比位置）
        {
            // 把一个 3D 世界坐标点，转换成键盘平面上的 2D 归一化坐标
            var localPoint = m_Root.InverseTransformPoint(worldPoint);//把世界坐标转换成键盘 root 的本地坐标
            var projected = new Vector2(
                SwipeTrajectoryUtility.GetAxisValue(localPoint, m_PrimaryAxis),
                SwipeTrajectoryUtility.GetAxisValue(localPoint, m_SecondaryAxis));//从 3D 本地坐标里取两个轴，变成 2D，例如取x和z，就不要y轴

            return new Vector2(
                (projected.x - m_Min.x) / m_Size.x,
                (projected.y - m_Min.y) / m_Size.y);//这个点在键盘范围里的百分比位置
        }

        public static bool TryCreate(Transform root, IList<VRKeyboardKey> keys, out SwipeKeyboardLayout layout)//out：输出创建好的布局
        {
            layout = null;
            if (root == null || keys == null)
                return false;

            // 收集所有字母键中心点的本地坐标，同时计算整体包围盒。
            var localPositions = new Dictionary<char, Vector3>();
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);//先设键盘字母区域的最小点为无穷小
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);//先设键盘字母区域的最大点为无穷大

            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (!TryGetLetter(key, out var letter))//如果不是字母键，就跳过
                    continue;

                var keyCollider = key.pressCollider != null ? key.pressCollider : key.GetComponent<BoxCollider>();//有按键碰撞盒，用碰撞盒中心点没有碰撞盒，用按键物体的位置
                var worldCenter = keyCollider != null
                    ? keyCollider.transform.TransformPoint(keyCollider.center)
                    : key.transform.position;

                var localCenter = root.InverseTransformPoint(worldCenter);//按键中心的世界坐标转换成键盘 root 的本地坐标

                localPositions[letter] = localCenter;
                min = Vector3.Min(min, localCenter);
                max = Vector3.Max(max, localCenter);
            }

            if (localPositions.Count < 2)
                return false;

            var ranges = max - min;
            // 自动选择跨度最大的两个轴作为键盘二维平面，适配不同朝向的键盘 prefab。
            var primaryAxis = GetLargestAxis(ranges, -1);//找最大跨度，不排除任何轴。
            var secondaryAxis = GetLargestAxis(ranges, primaryAxis);//排除刚刚选出来的 X，再找第二大的轴。

            var projectedMin = new Vector2(//2D 键盘区域的左下角
                SwipeTrajectoryUtility.GetAxisValue(min, primaryAxis),//取 min 在 primaryAxis 轴上的值
                SwipeTrajectoryUtility.GetAxisValue(min, secondaryAxis));//取 min 在 secondaryAxis 轴上的值
            var projectedMax = new Vector2(//2D 键盘区域的右上角
                SwipeTrajectoryUtility.GetAxisValue(max, primaryAxis),//取 max 在 primaryAxis 轴上的值
                SwipeTrajectoryUtility.GetAxisValue(max, secondaryAxis));//取 max 在 secondaryAxis 轴上的值
            var projectedSize = projectedMax - projectedMin;//键盘 2D 区域宽度和高度

            if (projectedSize.x <= 0.0001f || projectedSize.y <= 0.0001f)
                return false;

            // 把每个字母键的位置归一化，后续轨迹点和模板都使用同一坐标系。
            var normalizedPositions = new Dictionary<char, Vector2>(localPositions.Count);
            foreach (var pair in localPositions)
            {
                var point = new Vector2(//new Vector2(pair.Value.x, pair.Value.z)
                    SwipeTrajectoryUtility.GetAxisValue(pair.Value, primaryAxis),
                    SwipeTrajectoryUtility.GetAxisValue(pair.Value, secondaryAxis));
                normalizedPositions[pair.Key] = new Vector2(
                    (point.x - projectedMin.x) / projectedSize.x,
                    (point.y - projectedMin.y) / projectedSize.y);
            }

            var signature = BuildLayoutSignature(normalizedPositions);
            layout = new SwipeKeyboardLayout(
                root,//键盘根节点
                primaryAxis,
                secondaryAxis,
                projectedMin,
                projectedSize,
                normalizedPositions,//每个字母键的归一化 2D 坐标
                signature);//当前键盘布局签名
            return true;
        }

        static bool TryGetLetter(VRKeyboardKey key, out char letter)
        {
            return VRKeyboardKeyUtility.TryGetLetterKey(key, out letter);
        }

        static int GetLargestAxis(Vector3 value, int excludedAxis)
        {
            var bestAxis = -1;
            var bestValue = float.NegativeInfinity;
            for (var axis = 0; axis < 3; axis++)
            {
                if (axis == excludedAxis)
                    continue;

                var axisValue = SwipeTrajectoryUtility.GetAxisValue(value, axis);//取当前轴的值
                if (axisValue > bestValue)
                {
                    bestValue = axisValue;
                    bestAxis = axis;
                }
            }

            return bestAxis;
        }

        static string BuildLayoutSignature(Dictionary<char, Vector2> keyPositions)
        {
            // 用排序后的字母和坐标生成稳定字符串，再哈希成短签名。
            var letters = new List<char>(keyPositions.Keys);
            letters.Sort();//排序
            var builder = new StringBuilder(letters.Count * 18);
            for (var i = 0; i < letters.Count; i++)
            {//遍历每个字母拼接成例如a:0.0833,0.5000;
                var letter = letters[i];
                var point = keyPositions[letter];
                builder.Append(letter);
                builder.Append(':');
                builder.Append(point.x.ToString("F4", CultureInfo.InvariantCulture));//CultureInfo.InvariantCulture为了保证小数点永远用 . 有些地区系统里，小数可能用逗号
                builder.Append(',');
                builder.Append(point.y.ToString("F4", CultureInfo.InvariantCulture));
                builder.Append(';');
            }

            return ComputeFnv1A64(builder.ToString()).ToString("X16", CultureInfo.InvariantCulture);
        }

        static ulong ComputeFnv1A64(string value)
        {
            // FNV-1a 64 位哈希
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offsetBasis;
            var bytes = Encoding.UTF8.GetBytes(value);
            for (var i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= prime;
            }

            return hash;
        }
    }

    // Swipe 的轨迹工具：负责路径重采样、形状归一化、距离计算和轴值读取。
    public static class SwipeTrajectoryUtility
    {
        public static Vector2[] Resample(IList<Vector2> points, int sampleCount)
        {
            // 按路径长度等距重采样，让不同速度/采样率的滑动轨迹可以互相比较。
            sampleCount = Mathf.Max(2, sampleCount);
            var result = new Vector2[sampleCount];
            if (points == null || points.Count == 0)
                return result;

            if (points.Count == 1)
            {
                for (var i = 0; i < result.Length; i++)
                    result[i] = points[0];
                return result;
            }

            var cumulativeLengths = new float[points.Count];
            for (var i = 1; i < points.Count; i++)
                cumulativeLengths[i] = cumulativeLengths[i - 1] + Vector2.Distance(points[i - 1], points[i]);

            var totalLength = cumulativeLengths[cumulativeLengths.Length - 1];
            if (totalLength <= 0.000001f)
            {
                for (var i = 0; i < result.Length; i++)
                    result[i] = points[0];
                return result;
            }

            var segment = 1;
            for (var sample = 0; sample < sampleCount; sample++)
            {
                // 找到目标长度所在的原始线段，并在线段内插值。
                var targetLength = totalLength * sample / (sampleCount - 1f);
                while (segment < cumulativeLengths.Length - 1 && cumulativeLengths[segment] < targetLength)
                    segment++;

                var startLength = cumulativeLengths[segment - 1];
                var endLength = cumulativeLengths[segment];
                var t = endLength > startLength
                    ? (targetLength - startLength) / (endLength - startLength)
                    : 0f;
                result[sample] = Vector2.Lerp(points[segment - 1], points[segment], t);
            }

            return result;
        }

        public static Vector2[] NormalizeShape(IList<Vector2> points)
        {
            // 去掉绝对位置和整体尺度，只保留轨迹形状，用于和模板形状比较。
            if (points == null)
                return Array.Empty<Vector2>();

            var result = new Vector2[points.Count];
            if (points.Count == 0)
                return result;

            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var i = 0; i < points.Count; i++)
            {
                min = Vector2.Min(min, points[i]);
                max = Vector2.Max(max, points[i]);
            }

            var center = (min + max) * 0.5f;
            var scale = Mathf.Max(max.x - min.x, max.y - min.y);
            if (scale <= 0.000001f)
                scale = 1f;

            for (var i = 0; i < points.Count; i++)
                result[i] = (points[i] - center) / scale;

            return result;
        }

        public static float MeanDistance(IList<Vector2> a, IList<Vector2> b)
        {
            // 两条等长轨迹逐点平均距离。
            if (a == null || b == null || a.Count == 0 || a.Count != b.Count)
                return float.PositiveInfinity;

            var distance = 0f;
            for (var i = 0; i < a.Count; i++)
                distance += Vector2.Distance(a[i], b[i]);
            return distance / a.Count;
        }

        public static float DynamicTimeWarpingDistance(IList<Vector2> a, IList<Vector2> b, int windowRadius)
        {
            // 动态时间规整距离：允许两条轨迹局部速度不同，但整体路径相似。
            if (a == null || b == null || a.Count == 0 || b.Count == 0)
                return float.PositiveInfinity;

            // 窗口限制比较范围，减少计算量，也避免过度扭曲匹配。
            windowRadius = Mathf.Max(windowRadius, Mathf.Abs(a.Count - b.Count));
            var previous = new float[b.Count + 1];
            var current = new float[b.Count + 1];
            var previousSteps = new int[b.Count + 1];
            var currentSteps = new int[b.Count + 1];
            for (var j = 0; j < previous.Length; j++)
                previous[j] = float.PositiveInfinity;
            previous[0] = 0f;

            for (var i = 1; i <= a.Count; i++)
            {
                for (var j = 0; j < current.Length; j++)
                {
                    current[j] = float.PositiveInfinity;
                    currentSteps[j] = 0;
                }

                var firstColumn = Mathf.Max(1, i - windowRadius);
                var lastColumn = Mathf.Min(b.Count, i + windowRadius);
                for (var j = firstColumn; j <= lastColumn; j++)
                {
                    // 从左上、上、左三个方向取最小累计代价。
                    var localDistance = Vector2.Distance(a[i - 1], b[j - 1]);
                    var bestCost = previous[j - 1];
                    var bestSteps = previousSteps[j - 1];
                    if (previous[j] < bestCost)
                    {
                        bestCost = previous[j];
                        bestSteps = previousSteps[j];
                    }
                    if (current[j - 1] < bestCost)
                    {
                        bestCost = current[j - 1];
                        bestSteps = currentSteps[j - 1];
                    }

                    current[j] = localDistance + bestCost;
                    currentSteps[j] = bestSteps + 1;
                }

                var swap = previous;
                previous = current;
                current = swap;
                var stepSwap = previousSteps;
                previousSteps = currentSteps;
                currentSteps = stepSwap;
            }

            return previous[b.Count] / Mathf.Max(1, previousSteps[b.Count]);
        }

        public static float PathLength(IList<Vector2> points)
        {
            // 计算一条 2D 折线路径的总长度。
            if (points == null)
                return 0f;

            var length = 0f;
            for (var i = 1; i < points.Count; i++)
                length += Vector2.Distance(points[i - 1], points[i]);
            return length;
        }

        public static float GetAxisValue(Vector3 value, int axis)
        {
            // 用数字轴索引读取 Vector3 分量，方便布局投影算法复用。
            switch (axis)
            {
                case 0:
                    return value.x;
                case 1:
                    return value.y;
                default:
                    return value.z;
            }
        }
    }
}
