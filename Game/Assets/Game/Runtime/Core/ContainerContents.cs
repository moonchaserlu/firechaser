using System;
using System.Collections.Generic;

namespace WildernessExtraction
{
    /// <summary>
    /// 容器里的物品格子：可堆叠的同种物品合成一格（子弹等，每格最多 maxStack 个），搜索时按格逐个揭示，
    /// 揭示过的物品留在容器里，玩家逐格取走（拖进背包或快捷转移）。只做数据，不关心计时；不会复制或丢失物品。
    /// </summary>
    public sealed class ContainerContents
    {
        /// <summary>每格基础搜索秒数 = 容器搜索时长 × 该系数；稀有度再乘倍率。</summary>
        public const float PerItemFactor = 0.5f;
        public const float MinItemSeconds = 0.25f;

        private sealed class Slot
        {
            public ItemDefinition Item;
            public int Quantity;
            public bool Revealed;
        }

        private readonly List<Slot> slots = new List<Slot>();
        private readonly List<ItemDefinition> flattened = new List<ItemDefinition>();

        public ContainerContents(IEnumerable<ItemDefinition> contents)
        {
            if (contents == null) return;
            foreach (var item in contents)
            {
                if (item == null) continue;
                var slot = item.IsStackable ? slots.Find(s => s.Item == item && s.Quantity < item.maxStack) : null;
                if (slot == null)
                {
                    slot = new Slot { Item = item };
                    slots.Add(slot);
                }

                slot.Quantity++;
            }

            Rebuild();
        }

        public event Action Changed;

        /// <summary>格子数。</summary>
        public int Count => slots.Count;
        public ItemDefinition this[int index] => slots[index].Item;
        public int QuantityAt(int index) => index >= 0 && index < slots.Count ? slots[index].Quantity : 0;
        /// <summary>容器里剩下的全部物品（逐件展开，含未揭示的）。</summary>
        public IReadOnlyList<ItemDefinition> Items => flattened;
        public bool IsRevealed(int index) => index >= 0 && index < slots.Count && slots[index].Revealed;
        public bool AllRevealed => NextHidden < 0;
        public bool IsEmpty => slots.Count == 0;
        public int RevealedCount { get { var n = 0; foreach (var s in slots) if (s.Revealed) n++; return n; } }

        /// <summary>下一格待揭示的下标；全部揭示后为 -1。</summary>
        public int NextHidden => slots.FindIndex(s => !s.Revealed);

        /// <summary>揭示下一格，返回其下标；已全部揭示返回 -1。</summary>
        public int RevealNext()
        {
            var index = NextHidden;
            if (index < 0) return -1;
            slots[index].Revealed = true;
            Changed?.Invoke();
            return index;
        }

        public void RevealAll()
        {
            if (AllRevealed) return;
            foreach (var slot in slots) slot.Revealed = true;
            Changed?.Invoke();
        }

        /// <summary>把一格（已揭示）尽量放进背包，返回放进去的件数；放不下的留在这一格里。</summary>
        public int TakeAt(int index, Inventory inventory)
        {
            if (inventory == null || !IsRevealed(index)) return 0;
            var moved = MoveSlot(index, inventory);
            if (moved > 0)
            {
                Rebuild();
                Changed?.Invoke();
            }

            return moved;
        }

        /// <summary>按顺序取走所有已揭示物品，放不下的留下。Left 只统计已揭示却没放下的件数。</summary>
        public TransferResult TakeAllRevealed(Inventory inventory)
        {
            var moved = 0;
            var left = 0;
            for (var i = 0; i < slots.Count;)
            {
                if (!slots[i].Revealed) { i++; continue; }
                var count = slots.Count;
                moved += MoveSlot(i, inventory);
                if (slots.Count == count)
                {
                    left += slots[i].Quantity;
                    i++;
                }
            }

            if (moved > 0)
            {
                Rebuild();
                Changed?.Invoke();
            }

            return new TransferResult(moved, left);
        }

        /// <summary>某格的搜索秒数：越稀有越久（一组子弹按一格算）。</summary>
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

        private int MoveSlot(int index, Inventory inventory)
        {
            var slot = slots[index];
            var moved = 0;
            while (slot.Quantity > 0 && inventory.TryAdd(slot.Item))
            {
                slot.Quantity--;
                moved++;
            }

            if (slot.Quantity == 0) slots.RemoveAt(index);
            return moved;
        }

        private void Rebuild()
        {
            flattened.Clear();
            foreach (var slot in slots)
            {
                for (var i = 0; i < slot.Quantity; i++) flattened.Add(slot.Item);
            }
        }
    }
}
