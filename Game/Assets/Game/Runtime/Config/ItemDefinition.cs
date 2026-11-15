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
        [Tooltip("物品图标（透明底）。为空时界面退回颜色块加文字。由 ItemIconSetup 按 Icon_<itemId>.png 绑定。")]
        public Texture2D icon;

        [Header("使用（快捷栏 3–8 / 背包内 U）")]
        [Tooltip("使用读条秒数；0 表示不可使用。")]
        [Min(0f)] public float useDuration;
        [Min(0f)] public float healAmount;
        public bool stopsBleeding;

        public bool IsStackable => category != ItemCategory.Weapon && maxStack > 1;
        public bool IsUsable => category != ItemCategory.Weapon && useDuration > 0f && (healAmount > 0f || stopsBleeding);
    }
}
