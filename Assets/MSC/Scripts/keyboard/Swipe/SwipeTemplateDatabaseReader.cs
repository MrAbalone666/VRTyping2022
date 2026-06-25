using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VRTyping.Keyboard
{
    // 读取滑词模板数据库，把二进制 TextAsset 还原成运行时可用的数据结构。
    public static class SwipeTemplateDatabaseReader
    {
        public static bool TryLoad(TextAsset asset, out SwipeTemplateDatabase database, out string error)
        {
            database = null;
            error = string.Empty;
            if (asset == null)
            {
                error = "Template database asset is missing.";
                return false;
            }

            try
            {
                // 数据库通常作为 .bytes/TextAsset 放在 Resources 或 Inspector 引用中。
                using (var stream = new MemoryStream(asset.bytes, false))
                using (var reader = new BinaryReader(stream))
                {
                    // 先检查魔数和版本，避免把错误文件当成模板数据库读取。
                    if (reader.ReadInt32() != SwipeTemplateDatabase.Magic ||
                        reader.ReadInt32() != SwipeTemplateDatabase.Version)
                    {
                        throw new InvalidDataException("Invalid swipe template database header.");
                    }

                    var signature = reader.ReadString();
                    var pointCount = reader.ReadInt32();
                    var templateCount = reader.ReadInt32();
                    var templates = new List<SwipeWordTemplate>(templateCount);
                    for (var i = 0; i < templateCount; i++)
                    {
                        // 每个模板包含单词、词频、路径长度，以及两组等长轨迹点。
                        var word = reader.ReadString();
                        var frequency = reader.ReadSingle();
                        var pathLength = reader.ReadSingle();
                        var locations = ReadPoints(reader, pointCount);
                        var shapes = ReadPoints(reader, pointCount);
                        templates.Add(new SwipeWordTemplate(word, frequency, locations, shapes, pathLength));
                    }

                    database = new SwipeTemplateDatabase(signature, pointCount, templates);
                    return true;
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        static Vector2[] ReadPoints(BinaryReader reader, int count)
        {
            // 按 x/y float 成对读取固定数量的二维点。
            var points = new Vector2[count];
            for (var i = 0; i < count; i++)
                points[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            return points;
        }
    }
}
