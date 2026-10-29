using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 室内有灯即安全：整个室内判定体都算有效光，而不只是灯周围的半径。
    /// 常亮（安全屋），或跟随一盏室内灯的点亮状态（厂房随发电机）。没有灯的室内仍是黑暗。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BuildingReveal))]
    public sealed class IndoorLight : MonoBehaviour, ISafeLightArea
    {
        [SerializeField] private bool alwaysLit;
        [Tooltip("室内灯；它点亮时整个室内算有光。")]
        [SerializeField] private SafeLightSource linkedLamp;

        private BuildingReveal building;

        public bool IsLit => isActiveAndEnabled && (alwaysLit || (linkedLamp != null && linkedLamp.IsLit));

        public void Configure(bool lit, SafeLightSource lamp)
        {
            alwaysLit = lit;
            linkedLamp = lamp;
        }

        /// <summary>运行时切换常亮（任务模式下庇护所先断电，装上电池后恢复）。</summary>
        public void SetAlwaysLit(bool lit)
        {
            alwaysLit = lit;
        }

        public bool Contains(Vector3 worldPosition)
        {
            if (building == null) building = GetComponent<BuildingReveal>();
            return building.Contains(worldPosition);
        }

        private void OnEnable()
        {
            LightRegistry.Scene.Register(this);
        }

        private void OnDisable()
        {
            LightRegistry.Scene.Unregister(this);
        }
    }
}
