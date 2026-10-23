using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// Map01 的昼夜状态。默认永久黑暗；献祭身份牌后变为白天，作为一块覆盖全图的有效光登记到
    /// <see cref="LightRegistry"/>，黑暗暴露因此不再扣血，现有黑暗系统无需改动。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Map01Daylight : MonoBehaviour, ISafeLightArea
    {
        [SerializeField] private Light sun;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Color nightSunColor = new Color(0.55f, 0.62f, 0.85f);
        [SerializeField, Min(0f)] private float nightSunIntensity = 0.08f;
        [SerializeField] private Color nightAmbient = new Color(0.05f, 0.055f, 0.075f);
        [SerializeField] private Color nightBackground = new Color(0.02f, 0.02f, 0.03f);
        [SerializeField] private Color daySunColor = new Color(1f, 0.96f, 0.88f);
        [SerializeField, Min(0f)] private float daySunIntensity = 1.1f;
        [SerializeField] private Color dayAmbient = new Color(0.45f, 0.47f, 0.5f);
        [SerializeField] private Color dayBackground = new Color(0.62f, 0.7f, 0.76f);
        [Tooltip("树林萤火虫：只在黑夜出现。")]
        [SerializeField] private ParticleSystem[] fireflies = new ParticleSystem[0];
        [Tooltip("白天持续秒数；0 = 持续到本局结束。")]
        [SerializeField, Min(0f)] private float dayDuration;

        private float dayRemaining;

        public bool IsDay { get; private set; }
        bool ISafeLightArea.IsLit => IsDay && isActiveAndEnabled;

        public bool Contains(Vector3 worldPosition) => true;

        private void OnEnable()
        {
            LightRegistry.Scene.Register(this);
            Apply();
        }

        private void OnDisable()
        {
            LightRegistry.Scene.Unregister(this);
        }

        public void BeginDay()
        {
            IsDay = true;
            dayRemaining = dayDuration;
            Apply();
        }

        public void BeginNight()
        {
            IsDay = false;
            Apply();
        }

        private void Update()
        {
            if (IsDay && dayDuration > 0f)
            {
                dayRemaining -= Time.deltaTime;
                if (dayRemaining <= 0f) BeginNight();
            }
        }

        private void Apply()
        {
            if (sun != null)
            {
                sun.color = IsDay ? daySunColor : nightSunColor;
                sun.intensity = IsDay ? daySunIntensity : nightSunIntensity;
                sun.shadows = IsDay ? LightShadows.Soft : LightShadows.None;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = IsDay ? dayAmbient : nightAmbient;
            if (viewCamera != null)
            {
                viewCamera.backgroundColor = IsDay ? dayBackground : nightBackground;
            }

            foreach (var swarm in fireflies)
            {
                if (swarm == null) continue;
                if (IsDay) swarm.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                else if (!swarm.isPlaying) swarm.Play(true);
            }
        }
    }
}
