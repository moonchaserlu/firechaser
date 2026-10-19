using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>玩家本局背包，容量来自 RunRulesConfig。</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInventory : MonoBehaviour
    {
        [SerializeField] private RunRulesConfig rules;
        [SerializeField] private ItemDefinition[] startingItems;

        private Inventory inventory;
        private QuickSlotBar quickSlots;

        public Inventory Inventory => inventory ??= new Inventory(rules.inventoryCapacity);
        public QuickSlotBar QuickSlots => quickSlots ??= new QuickSlotBar(8);

        private void Awake()
        {
            if (startingItems == null) return;
            foreach (var item in startingItems)
            {
                if (item != null) Inventory.TryAdd(item);
            }
        }
    }
}
