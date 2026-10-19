using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>固定掉落表：按条目顺序产出，保证每局可复现。</summary>
    [CreateAssetMenu(menuName = "Wilderness/Loot Table", fileName = "LootTable")]
    public sealed class LootTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public ItemDefinition item;
            [Min(0)] public int count;
            [Min(0f)] public float weight;
            [Min(0)] public int minCount;
            [Min(0)] public int maxCount;

            public Entry(ItemDefinition item, int count)
            {
                this.item = item;
                this.count = count;
                weight = 0f;
                minCount = count;
                maxCount = count;
            }
        }

        public List<Entry> entries = new List<Entry>();
        [Min(0)] public int randomRolls;

        public List<ItemDefinition> CreateDrops()
        {
            return CreateDrops(null);
        }

        public List<ItemDefinition> CreateDrops(System.Random random)
        {
            var drops = new List<ItemDefinition>();
            if (randomRolls > 0 && random != null)
            {
                for (var roll = 0; roll < randomRolls; roll++)
                {
                    var selected = SelectWeighted(random);
                    if (selected.item == null) continue;
                    var minimum = Mathf.Max(0, selected.minCount);
                    var maximum = Mathf.Max(minimum, selected.maxCount);
                    var amount = random.Next(minimum, maximum + 1);
                    for (var i = 0; i < amount; i++) drops.Add(selected.item);
                }
                return drops;
            }

            foreach (var entry in entries)
            {
                if (entry.item == null)
                {
                    continue;
                }

                for (var i = 0; i < entry.count; i++)
                {
                    drops.Add(entry.item);
                }
            }

            return drops;
        }

        private Entry SelectWeighted(System.Random random)
        {
            var total = 0f;
            foreach (var entry in entries) if (entry.item != null) total += Mathf.Max(0f, entry.weight);
            if (total <= 0f) return default;
            var value = (float)random.NextDouble() * total;
            foreach (var entry in entries)
            {
                value -= Mathf.Max(0f, entry.weight);
                if (value <= 0f && entry.item != null) return entry;
            }
            return default;
        }
    }
}
