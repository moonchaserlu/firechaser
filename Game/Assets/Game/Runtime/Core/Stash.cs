using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>局外仓库：按物品类型累计数量。</summary>
    public sealed class Stash
    {
        private readonly Dictionary<ItemDefinition, int> counts = new Dictionary<ItemDefinition, int>();
        private readonly List<ItemDefinition> itemTypes = new List<ItemDefinition>();

        /// <summary>按首次入库顺序排列的物品类型。</summary>
        public IReadOnlyList<ItemDefinition> ItemTypes => itemTypes;
        public int TotalItems { get; private set; }
        public int TotalValue { get; private set; }

        public int CountOf(ItemDefinition item)
        {
            return item != null && counts.TryGetValue(item, out var count) ? count : 0;
        }

        public void Deposit(IEnumerable<ItemDefinition> items)
        {
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
                    itemTypes.Add(item);
                }

                TotalItems++;
                TotalValue += item.stashValue;
            }
        }
    }

    /// <summary>本轮只在当前运行会话中保留仓库（VerticalSlicePlan 7）；每次进入 Play Mode 重置。</summary>
    public static class SessionStash
    {
        public static Stash Current { get; private set; } = new Stash();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewPlaySession()
        {
            Current = new Stash();
        }
    }
}
