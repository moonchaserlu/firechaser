using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction.Shelter
{
    public enum ShelterStationKind
    {
        /// <summary>仓库：在仓库与背包之间搬物资。</summary>
        Stash,
        /// <summary>工作台：按配方合成。</summary>
        Workbench,
        /// <summary>出口：通往地面的楼梯，确认携带物资后出发。</summary>
        Exit
    }

    /// <summary>安全屋里的一处设施，只负责被找到和显示提示；操作由 <see cref="ShelterController"/> 判定。</summary>
    [DisallowMultipleComponent]
    public sealed class ShelterStation : MonoBehaviour
    {
        private static readonly List<ShelterStation> active = new List<ShelterStation>();

        [SerializeField] private ShelterStationKind kind;
        [SerializeField] private string displayName = "设施";

        public static IReadOnlyList<ShelterStation> Active => active;
        public ShelterStationKind Kind => kind;
        public string DisplayName => displayName;

        public void Configure(ShelterStationKind stationKind, string name)
        {
            kind = stationKind;
            displayName = name;
        }

        public float HorizontalDistanceTo(Vector3 position)
        {
            var delta = position - transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        public static ShelterStation FindNearest(Vector3 position, float range)
        {
            ShelterStation nearest = null;
            var best = range;
            foreach (var station in active)
            {
                var distance = station.HorizontalDistanceTo(position);
                if (distance <= best)
                {
                    best = distance;
                    nearest = station;
                }
            }

            return nearest;
        }

        public static ShelterStation Find(ShelterStationKind stationKind) => active.Find(s => s.kind == stationKind);

        private void OnEnable() => active.Add(this);

        private void OnDisable() => active.Remove(this);
    }
}
