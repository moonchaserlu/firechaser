using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction.Map01
{
    public enum MonsterBehavior
    {
        Patrol,
        RoomGuard,
        Harass
    }

    [Serializable]
    public sealed class LootEntry
    {
        public ItemDefinition item;
        public string itemId;
        public int value = 1;
        public float weight = 1f;
        [Tooltip("该物品只在不低于此深度档位的点位出现。")]
        public int minTier;
        [Tooltip("点位必须带有该标签；为空表示不限。")]
        public string requiredTag;
    }

    [Serializable]
    public sealed class MonsterEntry
    {
        public string monsterId;
        public MonsterBehavior behavior;
        [Tooltip("点位必须带有该标签（patrol / room_guard / harass）。")]
        public string requiredTag;
        public int threatCost = 1;
        public int minTier;
    }

    public readonly struct LootSpawn
    {
        public readonly string PointId;
        public readonly string ItemId;
        public readonly int Value;

        public LootSpawn(string pointId, string itemId, int value)
        {
            PointId = pointId;
            ItemId = itemId;
            Value = value;
        }
    }

    public readonly struct MonsterSpawn
    {
        public readonly string PointId;
        public readonly string MonsterId;
        public readonly MonsterBehavior Behavior;

        public MonsterSpawn(string pointId, string monsterId, MonsterBehavior behavior)
        {
            PointId = pointId;
            MonsterId = monsterId;
            Behavior = behavior;
        }
    }

    public sealed class GeneratedContent
    {
        public int Seed;
        public readonly List<LootSpawn> Loot = new List<LootSpawn>();
        public readonly List<MonsterSpawn> Monsters = new List<MonsterSpawn>();
        public readonly List<string> ActiveEvents = new List<string>();
        public readonly List<string> SpecialRooms = new List<string>();
        /// <summary>本局在救援完成后会激活的撤离候选点。开局时撤离仍不可用。</summary>
        public string ExtractionPointId;

        public int ValueAt(string pointId)
        {
            var total = 0;
            foreach (var loot in Loot)
            {
                if (loot.PointId == pointId) total += loot.Value;
            }

            return total;
        }
    }

    /// <summary>
    /// 固定地图上的内容随机化（指南 7 Phase B）。同一种子永远得到同样结果；
    /// 点位按 ID 排序后遍历，与场景层级顺序无关。几何和点位本身不随种子变化。
    /// 生成结果由服务器持有，客户端只接收已发现部分。
    /// </summary>
    public static class ContentGenerator
    {
        public const string GeneratorEventTag = "event_generator";

        public static GeneratedContent Generate(MapDefinition map, ContentTables tables, int seed)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (tables == null) throw new ArgumentNullException(nameof(tables));

            var content = new GeneratedContent { Seed = seed };
            var random = new System.Random(seed);
            var points = new List<SpawnPointData>(map.points);
            points.Sort((a, b) => string.CompareOrdinal(a.pointId, b.pointId));

            var extractions = new List<SpawnPointData>();
            foreach (var point in points)
            {
                // 每个点固定消耗相同数量的随机数之外的分支，用点位自己的子种子，增删其他点不影响它。
                var local = new System.Random(unchecked(seed * 486187739 + StableHash(point.pointId)));
                switch (point.type)
                {
                    case SpawnPointType.Loot:
                        GenerateLoot(point, tables, local, content);
                        break;
                    case SpawnPointType.Monster:
                        GenerateMonster(point, tables, local, content);
                        break;
                    case SpawnPointType.Event:
                        if (point.HasTag(GeneratorEventTag) || local.NextDouble() < tables.optionalEventChance)
                        {
                            content.ActiveEvents.Add(point.pointId);
                        }

                        break;
                    case SpawnPointType.SpecialRoom:
                        if (local.NextDouble() < 0.5)
                        {
                            content.SpecialRooms.Add(point.pointId);
                        }

                        break;
                    case SpawnPointType.ExtractionCandidate:
                        extractions.Add(point);
                        break;
                }
            }

            if (extractions.Count > 0)
            {
                content.ExtractionPointId = extractions[random.Next(extractions.Count)].pointId;
            }

            return content;
        }

        private static void GenerateLoot(SpawnPointData point, ContentTables tables, System.Random random, GeneratedContent content)
        {
            var tier = Mathf.Clamp(point.depthTier, 0, tables.lootCountByTier.Length - 1);
            if (random.NextDouble() > tables.lootActivationByTier[Mathf.Min(tier, tables.lootActivationByTier.Length - 1)])
            {
                return;
            }

            var candidates = new List<LootEntry>();
            var total = 0f;
            foreach (var entry in tables.loot)
            {
                if (entry.minTier <= tier && point.HasTag(entry.requiredTag) && entry.weight > 0f)
                {
                    candidates.Add(entry);
                    total += entry.weight;
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            var range = tables.lootCountByTier[tier];
            var count = Mathf.Min(random.Next(range.x, range.y + 1), Mathf.Max(1, point.capacity) * range.y);
            for (var i = 0; i < count; i++)
            {
                var roll = random.NextDouble() * total;
                foreach (var entry in candidates)
                {
                    roll -= entry.weight;
                    if (roll <= 0)
                    {
                        content.Loot.Add(new LootSpawn(point.pointId, entry.itemId, entry.value));
                        break;
                    }
                }
            }
        }

        private static void GenerateMonster(SpawnPointData point, ContentTables tables, System.Random random, GeneratedContent content)
        {
            var tier = Mathf.Clamp(point.depthTier, 0, tables.monsterActivationByTier.Length - 1);
            if (random.NextDouble() > tables.monsterActivationByTier[tier])
            {
                return;
            }

            var candidates = tables.monsters.FindAll(m => m.minTier <= point.depthTier && point.HasTag(m.requiredTag));
            if (candidates.Count == 0)
            {
                return;
            }

            var pick = candidates[random.Next(candidates.Count)];
            content.Monsters.Add(new MonsterSpawn(point.pointId, pick.monsterId, pick.behavior));
        }

        /// <summary>与平台无关的字符串哈希（string.GetHashCode 在不同运行间不稳定）。</summary>
        public static int StableHash(string text)
        {
            unchecked
            {
                var hash = (int)2166136261;
                foreach (var c in text)
                {
                    hash = (hash ^ c) * 16777619;
                }

                return hash;
            }
        }
    }
}
