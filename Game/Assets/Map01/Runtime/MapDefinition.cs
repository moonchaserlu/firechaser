using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>静态点位数据：地图的唯一事实来源，由场景中的 MapSpawnPoint 导出。</summary>
    [Serializable]
    public sealed class SpawnPointData
    {
        public string pointId;
        public SpawnPointType type;
        public Vector3 position;
        public float radius = 1.5f;
        public float weight = 1f;
        [Tooltip("由主安全屋出发的最短可行路径长度划分。")]
        public int depthTier;
        [Tooltip("主安全屋到该点的 NavMesh 最短路径长度（米）。")]
        public float pathLength;
        public string[] tags = Array.Empty<string>();
        public int capacity = 1;
        public string buildingId;

        public bool HasTag(string tag) => string.IsNullOrEmpty(tag) || Array.IndexOf(tags, tag) >= 0;
    }

    /// <summary>
    /// 指南 6.3 的 MapDefinition：地图 ID、版本、范围、主安全屋与全部点位。
    /// 深度档位按最短可行路径长度计算，不用直线距离；阈值是临时值，需要试玩调整。
    /// </summary>
    [CreateAssetMenu(menuName = "Wilderness/Map01/Map Definition", fileName = "MapDefinition")]
    public sealed class MapDefinition : ScriptableObject
    {
        public string mapId = Map01Layout.MapId;
        public int version = Map01Layout.Version;
        public Vector2 worldSize = new Vector2(Map01Layout.WorldWidth, Map01Layout.WorldDepth);
        public string safehouseId = "map01_safehouse";
        [Tooltip("路径长度 ≤ 该值为 Tier 0（安全屋周边与返程缓冲区）。")]
        public float tier0MaxPath = DepthTiers.DefaultTier0MaxPath;
        [Tooltip("路径长度 ≤ 该值为 Tier 1，其余为 Tier 2。")]
        public float tier1MaxPath = DepthTiers.DefaultTier1MaxPath;
        public List<SpawnPointData> points = new List<SpawnPointData>();

        public SpawnPointData Find(string pointId) => points.Find(p => p.pointId == pointId);
    }

    public static class DepthTiers
    {
        public const float DefaultTier0MaxPath = 90f;
        public const float DefaultTier1MaxPath = 360f;

        public static int FromPathLength(float pathLength, float tier0Max = DefaultTier0MaxPath, float tier1Max = DefaultTier1MaxPath)
        {
            if (pathLength <= tier0Max) return 0;
            return pathLength <= tier1Max ? 1 : 2;
        }
    }
}
