using UnityEngine;

namespace WildernessExtraction
{
    public enum ItemCategory { Consumable, Ammo, Material, Valuable, Weapon }
    public enum ItemRarity { Common, Uncommon, Rare }

    [System.Flags]
    public enum ItemTags { None = 0, Medical = 1, Food = 2, Ammo = 4, Part = 8, Quest = 16, Valuable = 32 }

    /// <summary>所有可搜刽物品的稳定数据定义。</summary>
    [CreateAssetMenu(menuName = "Wilderness/Item", fileName = "Item")]
    public sealed class ItemDefinition : ScriptableObject
    {
        public string itemId = "item";
        public string displayName = "物品";
        public ItemCategory category = ItemCategory.Material;
        public ItemRarity rarity = ItemRarity.Common;
        [Range(1, 2)] public int size = 1;
        [Min(1)] public int maxStack = 1;
        [Min(0f)] public float unitWeight;
        [Tooltip("成功撤离后的基础价值。")]
        [Min(0)] public int stashValue = 1;
        public ItemTags tags;
        [Tooltip("灰盒 HUD 槽位颜色。")]
        public Color iconColor = Color.white;

        public bool IsStackable => category != ItemCategory.Weapon && maxStack > 1;
    }
}
