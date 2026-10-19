using System;
using System.Collections.Generic;

namespace WildernessExtraction
{
    public sealed class InventoryStack
    {
        public InventoryStack(ItemDefinition item, int quantity) { Item = item; Quantity = quantity; }
        public ItemDefinition Item { get; }
        public int Quantity { get; internal set; }
        public int OccupiedSlots => Item.size;
    }

    /// <summary>玩家自定义的快捷栏。槽位保存物品类型，数量以背包实时数据为准。</summary>
    public sealed class QuickSlotBar
    {
        private readonly ItemDefinition[] slots;

        public QuickSlotBar(int capacity = 8)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            slots = new ItemDefinition[capacity];
        }

        public event Action Changed;
        public int Capacity => slots.Length;
        public int SelectedIndex { get; private set; } = -1;
        public ItemDefinition SelectedItem => SelectedIndex >= 0 ? slots[SelectedIndex] : null;
        public ItemDefinition this[int index] => index >= 0 && index < slots.Length ? slots[index] : null;

        public bool Assign(int index, ItemDefinition item)
        {
            if (index < 0 || index >= slots.Length || item == null) return false;
            slots[index] = item;
            SelectedIndex = index;
            Changed?.Invoke();
            return true;
        }

        public bool Select(int index)
        {
            if (index < 0 || index >= slots.Length || slots[index] == null) return false;
            SelectedIndex = index;
            Changed?.Invoke();
            return true;
        }

        public void Clear(int index)
        {
            if (index < 0 || index >= slots.Length) return;
            slots[index] = null;
            if (SelectedIndex == index) SelectedIndex = -1;
            Changed?.Invoke();
        }
    }

    /// <summary>固定槽位背包；相同物品可堆叠，两格物品占用连续逻辑容量。</summary>
    public sealed class Inventory
    {
        private readonly List<InventoryStack> stacks = new List<InventoryStack>();
        private readonly List<ItemDefinition> flattened = new List<ItemDefinition>();

        public Inventory(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Capacity = capacity;
        }

        public event Action Changed;

        public int Capacity { get; }
        public int Count => UsedSlots;
        public int ItemCount => flattened.Count;
        public int UsedSlots { get { var n = 0; foreach (var stack in stacks) n += stack.OccupiedSlots; return n; } }
        public int FreeSlots => Capacity - UsedSlots;
        public bool IsFull => FreeSlots == 0;
        public IReadOnlyList<ItemDefinition> Items => flattened;
        public IReadOnlyList<InventoryStack> Stacks => stacks;
        public float TotalWeight { get { var value = 0f; foreach (var stack in stacks) value += stack.Item.unitWeight * stack.Quantity; return value; } }

        public bool TryAdd(ItemDefinition item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            var stack = item.IsStackable ? stacks.Find(candidate => candidate.Item == item && candidate.Quantity < item.maxStack) : null;
            if (stack == null && FreeSlots < item.size) return false;
            if (stack == null) { stack = new InventoryStack(item, 0); stacks.Add(stack); }
            stack.Quantity++;
            flattened.Add(item);
            Changed?.Invoke();
            return true;
        }

        public bool DropStackAt(int index, out InventoryStack dropped)
        {
            dropped = null;
            if (index < 0 || index >= stacks.Count) return false;
            dropped = stacks[index];
            stacks.RemoveAt(index);
            for (var i = 0; i < dropped.Quantity; i++) flattened.Remove(dropped.Item);
            Changed?.Invoke();
            return true;
        }

        public int Remove(ItemDefinition item, int quantity)
        {
            if (item == null || quantity <= 0) return 0;
            var removed = 0;
            for (var i = stacks.Count - 1; i >= 0 && removed < quantity; i--)
            {
                var stack = stacks[i];
                if (stack.Item != item) continue;
                var take = Math.Min(stack.Quantity, quantity - removed);
                stack.Quantity -= take; removed += take;
                for (var j = 0; j < take; j++) flattened.Remove(item);
                if (stack.Quantity == 0) stacks.RemoveAt(i);
            }
            if (removed > 0) Changed?.Invoke();
            return removed;
        }

        public int CountOf(ItemDefinition item)
        {
            var count = 0;
            foreach (var existing in flattened)
            {
                if (existing == item)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>取出全部物品并清空背包。</summary>
        public List<ItemDefinition> TakeAll()
        {
            var taken = new List<ItemDefinition>(flattened);
            flattened.Clear();
            stacks.Clear();
            if (taken.Count > 0)
            {
                Changed?.Invoke();
            }

            return taken;
        }
    }
}
