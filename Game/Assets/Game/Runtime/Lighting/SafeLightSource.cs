using UnityEngine;

namespace WildernessExtraction
{
    public enum SafeLightKind
    {
        SafeHouse,
        FixedLamp,
        Campfire,
    }

    /// <summary>
    /// 有效安全光：玩家中心在 XZ 半径内即免除黑暗伤害。
    /// 点光源照射距离和地面边界环都由同一玩法半径驱动，保证视觉边缘与判定边缘一致。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SafeLightSource : MonoBehaviour, ISafeLightArea
    {
        private const int BoundarySegments = 64;
        private const float BoundaryHeight = 0.05f;

        [SerializeField] private DarknessConfig config;
        [SerializeField] private SafeLightKind kind = SafeLightKind.FixedLamp;
        [Tooltip("大于 0 时覆盖配置中的半径（火堆由自身配置写入）。")]
        [SerializeField, Min(0f)] private float radiusOverride;
        [SerializeField] private bool startLit = true;
        [Tooltip("可选：由玩法半径驱动照射距离的点光源。")]
        [SerializeField] private Light lightSource;
        [Tooltip("可选：标出安全边界的地面环。")]
        [SerializeField] private LineRenderer boundary;

        private bool lit;

        public SafeLightKind Kind => kind;
        public bool IsLit => lit && isActiveAndEnabled;

        public float Radius
        {
            get
            {
                if (radiusOverride > 0f) return radiusOverride;
                return config != null ? config.RadiusFor(kind) : 4f;
            }
        }

        private void Awake()
        {
            lit = startLit;
        }

        private void Start()
        {
            ApplyVisuals();
        }

        private void OnEnable()
        {
            LightRegistry.Scene.Register(this);
        }

        private void OnDisable()
        {
            LightRegistry.Scene.Unregister(this);
        }

        // OnValidate 中改写 LineRenderer 会触发 SendMessage 警告，这里只同步点光源；边界环由 Start 或 RefreshVisuals 绘制。
        private void OnValidate()
        {
            if (lightSource != null)
            {
                lightSource.range = Radius * (config != null ? config.lightRangeMultiplier : 1.2f);
            }
        }

        public bool Contains(Vector3 worldPosition)
        {
            var delta = worldPosition - transform.position;
            delta.y = 0f;
            var radius = Radius;
            return delta.sqrMagnitude <= radius * radius;
        }

        /// <summary>点亮或熄灭。熄灭后立即不再提供保护。</summary>
        public void SetLit(bool value)
        {
            lit = value;
            ApplyVisuals();
        }

        public void SetRadius(float radius)
        {
            radiusOverride = Mathf.Max(0f, radius);
            ApplyVisuals();
        }

        /// <summary>按当前半径和点亮状态刷新点光源与边界环。编辑器搭建工具也会调用。</summary>
        public void RefreshVisuals()
        {
            ApplyVisuals();
        }

        private void ApplyVisuals()
        {
            var on = Application.isPlaying ? lit : startLit;
            var radius = Radius;

            // 只在状态变化时切换 enabled：OnValidate 中切换渲染器会触发 SendMessage 警告。
            if (lightSource != null)
            {
                if (lightSource.enabled != on) lightSource.enabled = on;
                lightSource.range = radius * (config != null ? config.lightRangeMultiplier : 1.2f);
            }

            if (boundary != null)
            {
                if (boundary.enabled != on) boundary.enabled = on;
                boundary.useWorldSpace = false;
                boundary.loop = true;
                boundary.positionCount = BoundarySegments;
                for (var i = 0; i < BoundarySegments; i++)
                {
                    var angle = i * Mathf.PI * 2f / BoundarySegments;
                    boundary.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, BoundaryHeight, Mathf.Sin(angle) * radius));
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, Radius);
        }
    }
}
