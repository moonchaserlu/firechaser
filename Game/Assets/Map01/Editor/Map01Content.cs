using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// 阶段 3：从烘焙后的场景导出 MapDefinition。每个点位的深度档位由主安全屋出发的最短可行路径长度决定，
    /// 写回场景中的 MapSpawnPoint。内容表只在缺失时创建，之后由策划调数值，重建不会覆盖。
    /// </summary>
    public static class Map01Content
    {
        public const string DataFolder = "Assets/Map01/Data";
        public const string MapDefinitionPath = DataFolder + "/Map01_MapDefinition.asset";
        public const string ContentTablesPath = DataFolder + "/Map01_ContentTables.asset";
        private const string ItemsFolder = "Assets/Game/Data/Items";

        public static MapDefinition Export(Transform mapRoot, bool writeAssets)
        {
            var points = mapRoot.GetComponentsInChildren<MapSpawnPoint>(true);
            var spawn = Array.Find(points, p => p.Type == SpawnPointType.PlayerSpawn);
            if (spawn == null || !NavMesh.SamplePosition(spawn.transform.position, out var start, 2f, NavMesh.AllAreas))
            {
                throw new InvalidOperationException("找不到主安全屋出生点或它不在 NavMesh 上，无法计算路径深度。");
            }

            var map = ScriptableObject.CreateInstance<MapDefinition>();
            var report = new StringBuilder("[Map01] 点位路径深度：\n");
            Array.Sort(points, (a, b) => string.CompareOrdinal(a.PointId, b.PointId));
            foreach (var point in points)
            {
                var length = PathLength(start.position, point.transform.position);
                point.SetDepthTier(DepthTiers.FromPathLength(length, map.tier0MaxPath, map.tier1MaxPath));
                EditorUtility.SetDirty(point);
                map.points.Add(point.ToData(length));
                report.AppendLine($"  {point.PointId}\t{length:F0}m\tTier {point.DepthTier}");
            }

            Debug.Log(report.ToString());
            if (writeAssets)
            {
                EnsureFolder(DataFolder);
                var existing = AssetDatabase.LoadAssetAtPath<MapDefinition>(MapDefinitionPath);
                if (existing != null)
                {
                    // 保留策划调过的档位阈值，只刷新点位。
                    existing.points = map.points;
                    EditorUtility.SetDirty(existing);
                    map = existing;
                }
                else
                {
                    AssetDatabase.CreateAsset(map, MapDefinitionPath);
                }

                EnsureContentTables();
            }

            return map;
        }

        private static float PathLength(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(to, out var hit, 2f, NavMesh.AllAreas))
            {
                return float.PositiveInfinity;
            }

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(from, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                return float.PositiveInfinity;
            }

            var length = 0f;
            for (var i = 1; i < path.corners.Length; i++)
            {
                length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            }

            return length;
        }

        private static void EnsureContentTables()
        {
            if (AssetDatabase.LoadAssetAtPath<ContentTables>(ContentTablesPath) != null)
            {
                return;
            }

            var tables = ScriptableObject.CreateInstance<ContentTables>();
            // 回收物与辅助物品复用工程现有 ItemDefinition，价值随之同步。
            AddLoot(tables, "ScrapMetal", 8f, 0, null);
            AddLoot(tables, "Cloth", 6f, 0, null);
            AddLoot(tables, "CannedFood", 6f, 0, null);
            AddLoot(tables, "CannedFood", 6f, 0, "food");
            AddLoot(tables, "DirtyBandage", 4f, 0, null);
            AddLoot(tables, "DirtyBandage", 6f, 0, "medical");
            AddLoot(tables, "FirstAidSpray", 3f, 1, "medical");
            AddLoot(tables, "MechanicalParts", 5f, 1, null);
            AddLoot(tables, "MechanicalParts", 5f, 1, "tools");
            AddLoot(tables, "Chemicals", 3f, 1, null);
            AddLoot(tables, "OldBattery", 3f, 1, null);
            AddLoot(tables, "Watch", 2f, 2, null);
            AddLoot(tables, "DataDrive", 1f, 2, null);
            AddLoot(tables, "DataDrive", 3f, 1, "info");
            AddLoot(tables, "OldBattery", 6f, 2, "two_hand");

            tables.monsters.Add(new MonsterEntry { monsterId = "placeholder_patroller", behavior = MonsterBehavior.Patrol, requiredTag = "patrol", threatCost = 1 });
            tables.monsters.Add(new MonsterEntry { monsterId = "placeholder_room_guard", behavior = MonsterBehavior.RoomGuard, requiredTag = "room_guard", threatCost = 2 });
            tables.monsters.Add(new MonsterEntry { monsterId = "placeholder_harasser", behavior = MonsterBehavior.Harass, requiredTag = "guard", threatCost = 2, minTier = 1 });
            AssetDatabase.CreateAsset(tables, ContentTablesPath);
        }

        private static void AddLoot(ContentTables tables, string file, float weight, int minTier, string tag)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsFolder}/Item_{file}.asset");
            if (item == null)
            {
                Debug.LogWarning($"[Map01] 缺少物品 Item_{file}，内容表跳过该条。");
                return;
            }

            tables.loot.Add(new LootEntry { item = item, itemId = item.itemId, value = item.stashValue, weight = weight, minTier = minTier, requiredTag = tag });
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
