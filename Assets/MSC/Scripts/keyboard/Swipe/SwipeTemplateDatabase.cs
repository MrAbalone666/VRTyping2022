using System;
using System.Collections.Generic;

namespace VRTyping.Keyboard
{
    // 保存所有滑词模板的数据结构。Reader/Writer 会用固定二进制格式读写它。
    public sealed class SwipeTemplateDatabase
    {
        // 文件头魔数，用来快速判断二进制文件是不是 Swipe 模板数据库。
        internal const int Magic = 0x42445753;
        // 二进制格式版本，后续格式变更时可用它做兼容判断。
        internal const int Version = 1;

        // 生成模板时使用的键盘布局签名。
        public string layoutSignature { get; }
        // 每条模板轨迹的采样点数量。
        public int sampleCount { get; }
        // 所有可匹配单词的模板。
        public IReadOnlyList<SwipeWordTemplate> templates { get; }

        public SwipeTemplateDatabase(string layoutSignature, int sampleCount, IReadOnlyList<SwipeWordTemplate> templates)
        {
            this.layoutSignature = layoutSignature ?? string.Empty;
            this.sampleCount = sampleCount;
            this.templates = templates ?? Array.Empty<SwipeWordTemplate>();
        }
    }
}
