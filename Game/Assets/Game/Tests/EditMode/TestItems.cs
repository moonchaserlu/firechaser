using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction.Tests
{
    /// <summary>创建并统一销毁测试用的 ItemDefinition / LootTable。</summary>
    internal sealed class TestItems
    {
        private readonly List<Object> created = new List<Object>();

        public ItemDefinition Create(string id, int value)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = id;
            item.stashValue = value;
            created.Add(item);
            return item;
        }

        public LootTable CreateTable(params (ItemDefinition item, int count)[] entries)
        {
            var table = ScriptableObject.CreateInstance<LootTable>();
            foreach (var (item, count) in entries)
            {
                table.entries.Add(new LootTable.Entry(item, count));
            }

            created.Add(table);
            return table;
        }

        public void DestroyAll()
        {
            foreach (var asset in created)
            {
                Object.DestroyImmediate(asset);
            }

            created.Clear();
        }
    }
}
