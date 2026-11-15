using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 火焰表现：点光源强度与照射距离的轻微抖动，并让火焰外观跟随安全光的点亮状态。
    /// 只改视觉，玩法半径仍由 SafeLightSource 决定。其他脚本写入的亮度（如火堆将熄时的闪烁）会作为抖动的基准。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FireFlicker : MonoBehaviour
    {
        [SerializeField] private Light flameLight;
        [Tooltip("可选：熄灭时一并隐藏火焰外观。")]
        [SerializeField] private SafeLightSource source;
        [Tooltip("火焰外观（粒子），熄灭时隐藏。")]
        [SerializeField] private GameObject flame;
        [Tooltip("亮度抖动比例。")]
        [SerializeField, Range(0f, 0.5f)] private float intensityJitter = 0.18f;
        [Tooltip("照射距离抖动比例。")]
        [SerializeField, Range(0f, 0.2f)] private float rangeJitter = 0.04f;
        [SerializeField, Min(0.1f)] private float speed = 6f;

        private float seed;
        private float baseIntensity;
        private float baseRange;
        private float lastIntensity;
        private float lastRange;

        public float IntensityJitter => intensityJitter;
        public float BaseIntensity => baseIntensity;

        private void Awake()
        {
            seed = Random.value * 100f;
            if (flameLight == null) return;
            baseIntensity = lastIntensity = flameLight.intensity;
            baseRange = lastRange = flameLight.range;
        }

        private void LateUpdate()
        {
            var lit = source == null || source.IsLit;
            if (flame != null && flame.activeSelf != lit) flame.SetActive(lit);
            if (flameLight == null || !lit) return;

            if (!Mathf.Approximately(flameLight.intensity, lastIntensity)) baseIntensity = flameLight.intensity;
            if (!Mathf.Approximately(flameLight.range, lastRange)) baseRange = flameLight.range;

            var time = Time.time * speed;
            lastIntensity = baseIntensity * (1f + intensityJitter * (Mathf.PerlinNoise(seed, time) * 2f - 1f));
            lastRange = baseRange * (1f + rangeJitter * (Mathf.PerlinNoise(time, seed) * 2f - 1f));
            flameLight.intensity = lastIntensity;
            flameLight.range = lastRange;
        }
    }
}
