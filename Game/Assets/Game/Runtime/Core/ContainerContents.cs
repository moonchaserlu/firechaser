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

        /// <summary>箱子网格 3×4；尸体 1×5（见 <see cref="LootContainer"/>）。随机内容超出时往下加行。</summary>
        public const int GridWidth = 3;
        public const int GridHeight = 4;
        public const int CorpseWidth = 1;
        public const int CorpseHeight = 5;

        private readonly List<Slot> slots = new List<Slot>();
        private readonly List<ItemDefinition> flattened = new List<ItemDefinition>();
        private readonly GridLayout<Slot> layout;

        public ContainerContents(IEnumerable<ItemDefinition> contents) : this(contents, GridWidth, GridHeight)
        {
        }

        public ContainerContents(IEnumerable<ItemDefinition> contents, int width, int height)
        {
            layout = new GridLayout<Slot>(Math.Max(1, width), Math.Max(1, height));
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

            foreach (var slot in slots)
            {
                while (!layout.AutoPlace(slot, SizeOf(slot.Item))) layout.Grow(layout.Height + 1);
            }

            Rebuild();
        }

        public int Width => layout.Width;
        public int Height => layout.Height;

        /// <summary>第 index 格在网格里的位置。</summary>
        public GridPlacement PlacementAt(int index) => layout[slots[index]];

        /// <summary>网格 (x, y) 上是第几格；空的为 -1。</summary>
        public int IndexAt(int x, int y)
        {
            var slot = layout.At(x, y);
            return slot == null ? -1 : slots.IndexOf(slot);
        }

        /// <summary>在箱子里挪动第 index 格（已揭示的才能挪）。</summary>
        public bool MoveSlot(int index, int x, int y, bool rotated)
        {
            if (!IsRevealed(index)) return false;
            if (!layout.TryPlace(slots[index], SizeOf(slots[index].Item), x, y, rotated)) return false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>(x, y) 放得下这件东西吗（rotated 竖放）。</summary>
        public bool Fits(ItemDefinition item, int x, int y, bool rotated) => item != null && layout.Fits(x, y, SizeOf(item), rotated);

        private static int SizeOf(ItemDefinition item) => item != null ? Math.Max(1, item.size) : 1;

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

        /// <summary>
        /// 把物品放回容器（容器不限格数）：先叠到同种、还没满的已揭示格子里，再开新格。放进来的都算已揭示。返回放进去的件数。
        /// </summary>
        public int Store(ItemDefinition item, int quantity) => Store(item, quantity, -1, -1, false);

        /// <summary>放回容器；x, y ≥ 0 时新开的格子优先放在那里（rotated 竖放）。箱子满了就少放，返回放进去的件数。</summary>
        public int Store(ItemDefinition item, int quantity, int x, int y, bool rotated)
        {
            if (item == null || quantity <= 0) return 0;
            var left = quantity;
            if (item.IsStackable)
            {
                foreach (var slot in slots)
                {
                    if (left == 0) break;
                    if (slot.Item != item || !slot.Revealed || slot.Quantity >= item.maxStack) continue;
                    var add = Math.Min(left, item.maxStack - slot.Quantity);
                    slot.Quantity += add;
                    left -= add;
                }
            }

            while (left > 0)
            {
                var add = item.IsStackable ? Math.Min(left, item.maxStack) : 1;
                var slot = new Slot { Item = item, Quantity = add, Revealed = true };
                var placed = x >= 0 && y >= 0 && layout.TryPlace(slot, SizeOf(item), x, y, rotated);
                if (!placed) placed = layout.AutoPlace(slot, SizeOf(item));
                if (!placed) break;
                slots.Add(slot);
                left -= add;
                x = y = -1;
            }

            var stored = quantity - left;
            if (stored > 0)
            {
                Rebuild();
                Changed?.Invoke();
            }

            return stored;
        }

        /// <summary>箱子还能放下几件这种东西（先叠已有的格子，再算空格）。</summary>
        public int RoomFor(ItemDefinition item)
        {
            if (item == null) return 0;
            var room = 0;
            if (item.IsStackable)
            {
                foreach (var slot in slots)
                {
                    if (slot.Item == item && slot.Revealed) room += Math.Max(0, item.maxStack - slot.Quantity);
                }
            }

            var freeArea = layout.Area - layout.UsedArea;
            room += freeArea / SizeOf(item) * (item.IsStackable ? item.maxStack : 1);
            return room;
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

            if (slot.Quantity == 0)
            {
                slots.RemoveAt(index);
                layout.Remove(slot);
            }

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
