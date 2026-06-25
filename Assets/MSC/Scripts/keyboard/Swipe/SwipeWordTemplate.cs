using System.Text;
using UnityEngine;

namespace VRTyping.Keyboard
{
    // 一个滑词模板：保存某个单词在当前键盘布局上的理想滑动轨迹。
    public sealed class SwipeWordTemplate
    {
        public string word { get; }
        // 去掉连续重复字母后的单词，例如 "letter" -> "leter"，便于和滑过键序列比较。
        public string collapsedWord { get; }
        // 词频分数，识别时用于让常见词略微优先。
        public float frequencyScore { get; }
        // 保留键盘绝对位置的重采样轨迹点。
        public Vector2[] locationPoints { get; }
        // 去掉位置和尺度后的形状轨迹点。
        public Vector2[] shapePoints { get; }
        // locationPoints 的路径长度，用于比较滑动长短。
        public float pathLength { get; }

        public SwipeWordTemplate(string word, float frequencyScore, Vector2[] locationPoints, Vector2[] shapePoints, float pathLength)
        {
            this.word = word;
            collapsedWord = CollapseRepeatedLetters(word);
            this.frequencyScore = frequencyScore;
            this.locationPoints = locationPoints;
            this.shapePoints = shapePoints;
            this.pathLength = pathLength;
        }

        static string CollapseRepeatedLetters(string value)
        {
            // 连续相同字母在滑动输入里通常很难区分，所以模板里额外保存折叠版本。
            var builder = new StringBuilder(value.Length);
            var previous = default(char);
            for (var i = 0; i < value.Length; i++)
            {
                var letter = char.ToLowerInvariant(value[i]);
                if (letter == previous)
                    continue;
                builder.Append(letter);
                previous = letter;
            }
            return builder.ToString();
        }
    }
}
