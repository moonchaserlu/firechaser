using System;
using System.Collections.Generic;

namespace WildernessExtraction
{
    /// <summary>
    /// 容器里的物品格子：搜索时按顺序逐件揭示，揭示过的物品留在容器里，玩家逐件取走（拖进背包或快捷转移）。
    /// 只做数据，不关心计时；揭示和取走都不会复制或丢失物品。
    /// </summary>
    public sealed class ContainerContents
    {
        /// <summary>每件物品基础搜索秒数 = 容器搜索时长 × 该系数；稀有度再乘倍率。</summary>
        public const float PerItemFactor = 0.5f;
        public const float MinItemSeconds = 0.25f;

        private readonly List<ItemDefinition> items;
        private readonly List<bool> revealed;

        public ContainerContents(IEnumerable<ItemDefinition> contents)
        {
            items = new List<ItemDefinition>();
            revealed = new List<bool>();
            if (contents == null) return;
            foreach (var item in contents)
            {
                if (item == null) continue;
                items.Add(item);
                revealed.Add(false);
            }
        }

        public event Action Changed;

        public int Count => items.Count;
        public ItemDefinition this[int index] => items[index];
        public IReadOnlyList<ItemDefinition> Items => items;
        public bool IsRevealed(int index) => index >= 0 && index < revealed.Count && revealed[index];
        public bool AllRevealed => NextHidden < 0;
        public bool IsEmpty => items.Count == 0;
        public int RevealedCount { get { var n = 0; foreach (var r in revealed) if (r) n++; return n; } }

        /// <summary>下一件待揭示物品的下标；全部揭示后为 -1。</summary>
        public int NextHidden => revealed.IndexOf(false);

        /// <summary>揭示下一件物品，返回其下标；已全部揭示返回 -1。</summary>
        public int RevealNext()
        {
            var index = NextHidden;
            if (index < 0) return -1;
            revealed[index] = true;
            Changed?.Invoke();
            return index;
        }

        public void RevealAll()
        {
            if (AllRevealed) return;
            for (var i = 0; i < revealed.Count; i++) revealed[i] = true;
            Changed?.Invoke();
        }

        /// <summary>把一件已揭示的物品放进背包。未揭示、下标越界或背包放不下时返回 false，物品留在容器里。</summary>
        public bool TakeAt(int index, Inventory inventory)
        {
            if (inventory == null || !IsRevealed(index)) return false;
            if (!inventory.TryAdd(items[index])) return false;
            items.RemoveAt(index);
            revealed.RemoveAt(index);
            Changed?.Invoke();
            return true;
        }

        /// <summary>按顺序取走所有已揭示物品，放不下的留下。Left 只统计已揭示却没放下的件数。</summary>
        public TransferResult TakeAllRevealed(Inventory inventory)
        {
            var moved = 0;
            var left = 0;
            for (var i = 0; i < items.Count;)
            {
                if (!revealed[i]) { i++; continue; }
                if (inventory.TryAdd(items[i]))
                {
                    items.RemoveAt(i);
                    revealed.RemoveAt(i);
                    moved++;
                }
                else
                {
                    left++;
                    i++;
                }
            }

            if (moved > 0) Changed?.Invoke();
            return new TransferResult(moved, left);
        }

        /// <summary>某件物品的搜索秒数：越稀有越久。</summary>
        public static float SearchSeconds(ItemDefinition item, float containerDuration)
        {
            var baseSeconds = Math.Max(MinItemSeconds, containerDuration * PerItemFactor);
            if (item == null) return baseSeconds;
            switch (item.rarity)
            {
                case ItemRarity.Rare: return baseSeconds * 2f;
                case ItemRarity.Uncommon: return baseSeconds * 1.5f;
                default: return baseSeconds;
            }
        }
    }
}
