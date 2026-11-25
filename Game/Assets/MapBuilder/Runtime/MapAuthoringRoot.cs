using UnityEngine;

namespace WildernessExtraction.MapBuilder
{
    /// <summary>
    /// 地图搭建器的工作根节点。手摆的素材放在“素材”子节点，点位放在“点位”子节点；
    /// 搭建器只在带这个组件的场景里放东西，运行时不做任何事。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapAuthoringRoot : MonoBehaviour
    {
        public const string PropsGroup = "素材";
        public const string PointsGroup = "点位";
        public const string GroundGroup = "地面";

        [Tooltip("地图 ID，用于点位 ID 前缀和导出的 MapDefinition。")]
        public string mapId = "map02";

        [Min(0.1f), Tooltip("吸附网格（米）。")]
        public float grid = 1f;

        [Tooltip("地图范围（米，XZ），用于导出与检查。")]
        public Vector2 worldSize = new Vector2(120f, 120f);

        public Transform Group(string name)
        {
            var child = transform.Find(name);
            if (child != null)
            {
                return child;
            }

            child = new GameObject(name).transform;
            child.SetParent(transform, false);
            return child;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(transform.position, new Vector3(worldSize.x, 0.1f, worldSize.y));
        }
    }
}
