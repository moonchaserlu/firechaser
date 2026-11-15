using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 树林里的一团萤火虫：一圈微弱的冷暖色光。玩家（水平距离）在半径内即算处于有效光中，不受黑暗伤害。
    /// 只在黑夜出现；白天由 <see cref="Map01Daylight"/> 关掉（白天全图本来就安全）。
    /// 粒子、点光源和地面边界环都由同一个半径驱动，视觉边缘就是判定边缘。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FireflySwarm : MonoBehaviour, ISafeLightArea
    {
        private const int BoundarySegments = 48;

        [Tooltip("萤火虫光照的玩法半径（米）。")]
        [SerializeField, Min(0.5f)] private float radius = 4.5f;
        [SerializeField] private ParticleSystem particles;
        [SerializeField] private Light glow;
        [SerializeField] private LineRenderer boundary;

        private bool visible = true;

        public float Radius => radius;
        public bool IsLit => visible && isActiveAndEnabled;
        public ParticleSystem Particles => particles;

        public void Configure(float swarmRadius, ParticleSystem system, Light light, LineRenderer ring)
        {
            radius = swarmRadius;
            particles = system;
            glow = light;
            boundary = ring;
            ApplyVisuals();
        }

        public bool Contains(Vector3 worldPosition)
        {
            var delta = worldPosition - transform.position;
            delta.y = 0f;
            return delta.sqrMagnitude <= radius * radius;
        }

        /// <summary>夜里显示并提供光照；白天隐藏。</summary>
        public void SetVisible(bool value)
        {
            visible = value;
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

        private void ApplyVisuals()
        {
            if (particles != null)
            {
                if (!visible) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                else if (Application.isPlaying && !particles.isPlaying) particles.Play(true);
            }

            if (glow != null)
            {
                glow.enabled = visible;
                glow.range = radius * 1.3f;
            }

            if (boundary != null)
            {
                boundary.enabled = visible;
                boundary.useWorldSpace = false;
                boundary.loop = true;
                boundary.positionCount = BoundarySegments;
                for (var i = 0; i < BoundarySegments; i++)
                {
                    var angle = i * Mathf.PI * 2f / BoundarySegments;
                    boundary.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0.05f, Mathf.Sin(angle) * radius));
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
