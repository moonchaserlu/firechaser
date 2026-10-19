using System.Collections.Generic;

namespace WildernessExtraction
{
    public readonly struct TransferResult
    {
        public TransferResult(int moved, int left)
        {
            Moved = moved;
            Left = left;
        }

        public int Moved { get; }
        /// <summary>因背包已满而留在容器中的数量。</summary>
        public int Left { get; }
        public bool BlockedByFullInventory => Left > 0;
    }

    /// <summary>容器内尚未拾取的物品。转移是逐件移动，不会复制或丢失物品。</summary>
    public sealed class LootPile
    {
        private readonly List<ItemDefinition> remaining;

        public LootPile(IEnumerable<ItemDefinition> items)
        {
            remaining = new List<ItemDefinition>(items);
            remaining.RemoveAll(item => item == null);
        }

        public IReadOnlyList<ItemDefinition> Remaining => remaining;
        public bool IsEmpty => remaining.Count == 0;

        public TransferResult TransferTo(Inventory inventory)
        {
            var moved = 0;
            while (remaining.Count > 0 && inventory.TryAdd(remaining[0]))
            {
                remaining.RemoveAt(0);
                moved++;
            }

            return new TransferResult(moved, remaining.Count);
        }
    }
}
