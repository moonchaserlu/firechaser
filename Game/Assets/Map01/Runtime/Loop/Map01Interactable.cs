using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction.Map01
{
    public enum Map01Station
    {
        RecycleTerminal,
        ShopTerminal,
        Generator,
        BatterySlot,
        Altar,
        /// <summary>庇护所炉火：Q01 提交燃料、Q03 清点药品。</summary>
        Hearth,
        /// <summary>可阅读的剧情记录。</summary>
        Record,
        /// <summary>庇护所旧地图：Q10 对照运送记录。</summary>
        OldMap,
        /// <summary>庇护所工作台：按配方合成物品。</summary>
        Workbench
    }

    /// <summary>
    /// 地图设施（回收台、商店、发电机、电池槽）。只负责被找到和显示提示；
    /// 交互结果全部由 <see cref="Map01Authority"/> 判定。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Map01Interactable : MonoBehaviour
    {
        private static readonly List<Map01Interactable> active = new List<Map01Interactable>();

        [SerializeField] private string entityId;
        [SerializeField] private Map01Station station;

        public static IReadOnlyList<Map01Interactable> Active => active;
        public string EntityId => entityId;
        public Map01Station Station => station;

        public void Configure(string id, Map01Station kind)
        {
            entityId = id;
            station = kind;
        }

        public float HorizontalDistanceTo(Vector3 position)
        {
            var delta = position - transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        public static Map01Interactable FindNearest(Vector3 position, float range)
        {
            Map01Interactable nearest = null;
            var best = range;
            foreach (var candidate in active)
            {
                var distance = candidate.HorizontalDistanceTo(position);
                if (distance <= best)
                {
                    best = distance;
                    nearest = candidate;
                }
            }

            return nearest;
        }

        public static Map01Interactable Find(string id) => active.Find(i => i.entityId == id);

        public static bool IsQuestStation(Map01Station kind) => kind == Map01Station.Hearth || kind == Map01Station.Record || kind == Map01Station.OldMap;

        private void OnEnable()
        {
            active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
        }
    }
}
