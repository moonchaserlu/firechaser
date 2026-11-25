using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using WildernessExtraction.Map01;

namespace WildernessExtraction.MapBuilder.EditorTools
{
    /// <summary>
    /// 点位刷子：在地图上放出生 / 撤离 / 物资 / 怪物 / 事件 / 特殊房间点，复用 Map01 的 MapSpawnPoint 与 MapDefinition，
    /// 所以导出的数据可以直接交给现有的 ContentGenerator 按种子刷内容。
    /// </summary>
    public static class MapBuilderPoints
    {
        public static readonly SpawnPointType[] Types =
        {
            SpawnPointType.PlayerSpawn, SpawnPointType.ExtractionCandidate, SpawnPointType.Loot,
            SpawnPointType.Monster, SpawnPointType.Event, SpawnPointType.SpecialRoom
        };

        public static string Label(SpawnPointType type) => type switch
        {
            SpawnPointType.PlayerSpawn => "出生点",
            SpawnPointType.ExtractionCandidate => "撤离点",
            SpawnPointType.Loot => "物资点",
            SpawnPointType.Monster => "怪物点",
            SpawnPointType.Event => "事件点",
            _ => "特殊房间"
        };

        private static string Prefix(SpawnPointType type) => type switch
        {
            SpawnPointType.PlayerSpawn => "spawn",
            SpawnPointType.ExtractionCandidate => "extract",
            SpawnPointType.Loot => "loot",
            SpawnPointType.Monster => "monster",
            SpawnPointType.Event => "event",
            _ => "room"
        };

        public static float DefaultRadius(SpawnPointType type) => type switch
        {
            SpawnPointType.ExtractionCandidate => 3f,
            SpawnPointType.Monster => 4f,
            SpawnPointType.Event => 2.5f,
            SpawnPointType.SpecialRoom => 5f,
            _ => 1.5f
        };

        public static UnityEngine.Color PointColor(SpawnPointType type) => type switch
        {
            SpawnPointType.PlayerSpawn => UnityEngine.Color.cyan,
            SpawnPointType.ExtractionCandidate => new UnityEngine.Color(0.6f, 0.3f, 1f),
            SpawnPointType.Loot => UnityEngine.Color.yellow,
            SpawnPointType.Monster => UnityEngine.Color.red,
            SpawnPointType.Event => new UnityEngine.Color(1f, 0.5f, 0f),
            _ => UnityEngine.Color.green
        };

        public static MapSpawnPoint[] All(MapAuthoringRoot root)
        {
            return root == null ? Array.Empty<MapSpawnPoint>() : root.GetComponentsInChildren<MapSpawnPoint>(true);
        }

        public static string NextId(MapAuthoringRoot root, SpawnPointType type)
        {
            var used = new HashSet<string>(All(root).Select(p => p.PointId));
            for (var i = 1; ; i++)
            {
                var id = $"{root.mapId}_{Prefix(type)}_{i:00}";
                if (!used.Contains(id))
                {
                    return id;
                }
            }
        }

        public static MapSpawnPoint Place(MapAuthoringRoot root, SpawnPointType type, Vector3 position, float radius, string[] tags)
        {
            var group = root.Group(MapAuthoringRoot.PointsGroup);
            var typeGroup = group.Find(Label(type));
            if (typeGroup == null)
            {
                var go = new GameObject(Label(type));
                Undo.RegisterCreatedObjectUndo(go, "地图搭建器：新建点位分组");
                go.transform.SetParent(group, false);
                typeGroup = go.transform;
            }

            var id = NextId(root, type);
            var pointObject = new GameObject(id);
            pointObject.transform.SetParent(typeGroup, false);
            pointObject.transform.position = position;
            var point = pointObject.AddComponent<MapSpawnPoint>();
            point.Configure(new PointSpec
            {
                Id = id, Type = type, Position = new Vector2(position.x, position.z), Radius = radius,
                Tags = tags ?? Array.Empty<string>()
            });
            Undo.RegisterCreatedObjectUndo(pointObject, "地图搭建器：放置" + Label(type));
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            return point;
        }

        public static string DefinitionPath(MapAuthoringRoot root)
        {
            var scenePath = root.gameObject.scene.path;
            var folder = string.IsNullOrEmpty(scenePath) ? "Assets/Maps" : System.IO.Path.GetDirectoryName(scenePath).Replace('\\', '/');
            return $"{folder}/{root.mapId}_MapDefinition.asset";
        }

        /// <summary>
        /// 写出 MapDefinition。有导航网格时按出生点出发的寻路长度分 Tier，没有时退回直线距离并在报告里提示。
        /// 已存在的资产只刷新点位，保留调过的 Tier 阈值。
        /// </summary>
        public static MapDefinition Export(MapAuthoringRoot root, string path, out string report)
        {
            var points = All(root);
            Array.Sort(points, (a, b) => string.CompareOrdinal(a.PointId, b.PointId));
            var spawn = Array.Find(points, p => p.Type == SpawnPointType.PlayerSpawn);
            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(path);
            var created = map == null;
            if (created)
            {
                map = ScriptableObject.CreateInstance<MapDefinition>();
            }

            map.mapId = root.mapId;
            map.version = Mathf.Max(1, map.version);
            map.worldSize = root.worldSize;
            map.safehouseId = root.mapId + "_safehouse";

            var text = new StringBuilder($"[地图搭建器] {root.mapId} 点位 {points.Length} 个\n");
            var hasStart = spawn != null && NavMesh.SamplePosition(spawn.transform.position, out _, 2f, NavMesh.AllAreas);
            if (spawn == null)
            {
                text.AppendLine("  没有出生点，路径深度全部按 0 计算。");
            }
            else if (!hasStart)
            {
                text.AppendLine("  没有导航网格（或出生点不在网格上），深度按直线距离估算；烘焙导航后再导出更准。");
            }

            map.points = new List<SpawnPointData>();
            foreach (var point in points)
            {
                var length = spawn == null ? 0f : hasStart ? PathLength(spawn.transform.position, point.transform.position)
                    : Vector3.Distance(spawn.transform.position, point.transform.position);
                if (float.IsPositiveInfinity(length))
                {
                    text.AppendLine($"  {point.PointId} 从出生点走不到，按 Tier 2 处理。");
                    length = map.tier1MaxPath + 1f;
                }

                Undo.RecordObject(point, "地图搭建器：写回点位深度");
                point.SetDepthTier(DepthTiers.FromPathLength(length, map.tier0MaxPath, map.tier1MaxPath));
                EditorUtility.SetDirty(point);
                map.points.Add(point.ToData(length));
                text.AppendLine($"  {point.PointId}\t{MapBuilderPoints.Label(point.Type)}\t{length:F0}m\tTier {point.DepthTier}");
            }

            if (created)
            {
                MapBuilderLibrary.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
                AssetDatabase.CreateAsset(map, path);
            }
            else
            {
                EditorUtility.SetDirty(map);
            }

            AssetDatabase.SaveAssets();
            report = text.ToString();
            return map;
        }

        public static float PathLength(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(from, out var a, 2f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out var b, 3f, NavMesh.AllAreas))
            {
                return float.PositiveInfinity;
            }

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
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

        /// <summary>用地图根下所有碰撞体烘焙导航，并把数据存成场景旁的资产。</summary>
        public static NavMeshData BakeNavMesh(MapAuthoringRoot root)
        {
            var surface = root.GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                surface = Undo.AddComponent<NavMeshSurface>(root.gameObject);
            }

            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            if (surface.navMeshData == null)
            {
                throw new InvalidOperationException("导航网格烘焙失败：地图根下没有可走的碰撞体。");
            }

            var path = DefinitionPath(root).Replace("_MapDefinition.asset", "_NavMesh.asset");
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.CreateAsset(surface.navMeshData, path);
            EditorUtility.SetDirty(surface);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            return surface.navMeshData;
        }
    }
}
