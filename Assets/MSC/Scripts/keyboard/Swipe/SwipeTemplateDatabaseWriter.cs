using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VRTyping.Keyboard
{
    // Editor 里生成模板数据库时使用：把模板列表序列化成紧凑的二进制数据。
    public static class SwipeTemplateDatabaseWriter
    {
        public static byte[] Serialize(string signature, int pointCount, IReadOnlyList<SwipeWordTemplate> templates)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                // 写入文件头，Reader 会用它验证格式和版本。
                writer.Write(SwipeTemplateDatabase.Magic);
                writer.Write(SwipeTemplateDatabase.Version);
                writer.Write(signature ?? string.Empty);
                writer.Write(pointCount);
                writer.Write(templates != null ? templates.Count : 0);
                if (templates != null)
                {
                    for (var i = 0; i < templates.Count; i++)
                    {
                        // 每个词模板按固定顺序写入，Reader 必须用相同顺序读取。
                        var template = templates[i];
                        writer.Write(template.word);
                        writer.Write(template.frequencyScore);
                        writer.Write(template.pathLength);
                        WritePoints(writer, template.locationPoints, pointCount);
                        WritePoints(writer, template.shapePoints, pointCount);
                    }
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        static void WritePoints(BinaryWriter writer, IReadOnlyList<Vector2> points, int expectedCount)
        {
            // 所有模板必须使用相同采样点数量，识别时才能直接逐点比较。
            if (points == null || points.Count != expectedCount)
                throw new InvalidDataException("Template point count does not match the database sample count.");

            for (var i = 0; i < points.Count; i++)
            {
                writer.Write(points[i].x);
                writer.Write(points[i].y);
            }
        }
    }
}
