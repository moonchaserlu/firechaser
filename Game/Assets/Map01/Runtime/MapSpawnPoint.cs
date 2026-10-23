using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 静态地图点位。<see cref="PointId"/> 是持久 ID，场景层级、节点名称调整都不会改变它；
    /// 点位本身不联网，由它生成的战利品、怪物和事件才是动态实体。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapSpawnPoint : MonoBehaviour
    {
        [SerializeField] private string pointId;
        [SerializeField] private SpawnPointType type;
        [SerializeField, Min(0f)] private float radius = 1.5f;
        [SerializeField, Min(0f)] private float weight = 1f;
        [SerializeField, Min(0)] private int depthTier;
        [SerializeField] private string[] tags = new string[0];
        [SerializeField, Min(1)] private int capacity = 1;
        [Tooltip("所在建筑 ID；为空表示室外。")]
        [SerializeField] private string buildingId;

        public string PointId => pointId;
        public SpawnPointType Type => type;
        public float Radius => radius;
        public float Weight => weight;
        public int DepthTier => depthTier;
        public string[] Tags => tags;
        public int Capacity => capacity;
        public string BuildingId => buildingId;

        public void Configure(PointSpec spec)
        {
            pointId = spec.Id;
            type = spec.Type;
            radius = spec.Radius;
            weight = spec.Weight;
            depthTier = spec.DepthTier;
            tags = spec.Tags;
            capacity = spec.Capacity;
            buildingId = spec.BuildingId;
        }

        /// <summary>由烘焙后的路径深度覆盖布局表里的初始档位。</summary>
        public void SetDepthTier(int tier)
        {
            depthTier = tier;
        }

        public SpawnPointData ToData(float pathLength)
        {
            return new SpawnPointData
            {
                pointId = pointId, type = type, position = transform.position, radius = radius, weight = weight,
                depthTier = depthTier, pathLength = pathLength, tags = (string[])tags.Clone(), capacity = capacity, buildingId = buildingId
            };
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = type switch
            {
                SpawnPointType.PlayerSpawn => Color.cyan,
                SpawnPointType.ExtractionCandidate => new Color(0.6f, 0.3f, 1f),
                SpawnPointType.Loot => Color.yellow,
                SpawnPointType.Monster => Color.red,
                SpawnPointType.Event => new Color(1f, 0.5f, 0f),
                _ => Color.green
            };
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
