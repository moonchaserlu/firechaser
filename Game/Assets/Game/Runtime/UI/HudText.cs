using System.Collections.Generic;
using System.Text;

namespace WildernessExtraction
{
    /// <summary>HUD 文案格式化。纯函数，便于 EditMode 测试。</summary>
    public static class HudText
    {
        public const string Separator = "、";

        /// <summary>按首次出现顺序汇总物品，例如“废金属×2、旧电池×1”；为空时返回“无”。</summary>
        public static string ItemSummary(IEnumerable<ItemDefinition> items)
        {
            var order = new List<ItemDefinition>();
            var counts = new Dictionary<ItemDefinition, int>();
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                if (counts.TryGetValue(item, out var count))
                {
                    counts[item] = count + 1;
                }
                else
                {
                    counts[item] = 1;
                    order.Add(item);
                }
            }

            if (order.Count == 0)
            {
                return "无";
            }

            var builder = new StringBuilder();
            foreach (var item in order)
            {
                if (builder.Length > 0)
                {
                    builder.Append(Separator);
                }

                builder.Append(item.displayName).Append('×').Append(counts[item]);
            }

            return builder.ToString();
        }

        public static string StashSummary(Stash stash)
        {
            if (stash == null || stash.TotalItems == 0)
            {
                return "仓库：空";
            }

            var builder = new StringBuilder();
            foreach (var item in stash.ItemTypes)
            {
                if (builder.Length > 0)
                {
                    builder.Append(Separator);
                }

                builder.Append(item.displayName).Append('×').Append(stash.CountOf(item));
            }

            return $"仓库（价值 {stash.TotalValue}）：{builder}";
        }

        public static string ResultTitle(RunResult result)
        {
            return result.Outcome == RunOutcome.Extracted ? "撤离成功" : "行动失败";
        }

        public static string ResultBody(RunResult result, Stash stash)
        {
            var items = ItemSummary(result.Items);
            var line = result.Outcome == RunOutcome.Extracted
                ? $"带回 {result.Items.Count} 件（价值 {result.Value}）：{items}"
                : $"你倒下了，丢失 {result.Items.Count} 件（价值 {result.Value}）：{items}\n仓库未发生变化";
            return $"{line}\n{StashSummary(stash)}\n\n按 E 或点击按钮开始下一局";
        }
    }
}
