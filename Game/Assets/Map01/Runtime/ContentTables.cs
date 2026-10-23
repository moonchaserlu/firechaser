using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>可随机的内容表。设计师调数值，生成器只读。</summary>
    [CreateAssetMenu(menuName = "Wilderness/Map01/Content Tables", fileName = "ContentTables")]
    public sealed class ContentTables : ScriptableObject
    {
        public List<LootEntry> loot = new List<LootEntry>();
        public List<MonsterEntry> monsters = new List<MonsterEntry>();
        [Tooltip("每个物资点按档位生成的件数下限/上限（索引 = Tier）。")]
        public Vector2Int[] lootCountByTier = { new Vector2Int(1, 1), new Vector2Int(1, 2), new Vector2Int(2, 3) };
        [Tooltip("每个物资点被激活的概率（索引 = Tier）。")]
        public float[] lootActivationByTier = { 0.8f, 0.75f, 0.7f };
        [Tooltip("怪物点被激活的概率（索引 = Tier）。")]
        public float[] monsterActivationByTier = { 0f, 0.6f, 0.85f };
        [Tooltip("发电机事件固定出现；其他事件点的激活概率。")]
        public float optionalEventChance = 0.5f;
    }
}
