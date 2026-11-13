using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>按 itemId 查找物品定义，供局外存档读写。由安全屋搭建工具扫描工程里的物品资产生成。</summary>
    [CreateAssetMenu(menuName = "Wilderness/Item Database", fileName = "ItemDatabase")]
    public sealed class ItemDatabase : ScriptableObject
    {
        public List<ItemDefinition> items = new List<ItemDefinition>();

        private Dictionary<string, ItemDefinition> lookup;

        public ItemDefinition Find(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            if (lookup == null || lookup.Count != items.Count)
            {
                lookup = new Dictionary<string, ItemDefinition>();
                foreach (var item in items)
                {
                    if (item != null && !lookup.ContainsKey(item.itemId)) lookup[item.itemId] = item;
                }
            }

            return lookup.TryGetValue(itemId, out var found) ? found : null;
        }
    }
}
