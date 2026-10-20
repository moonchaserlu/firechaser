using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 玩家火堆：作为 SafeLightSource 提供黑暗安全，燃烧固定时长，最后阶段闪烁，燃尽后立即失去保护。
    /// 只存在于本局，场景重载即清除。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SafeLightSource))]
    public sealed class Campfire : MonoBehaviour
    {
        private static readonly List<Campfire> burning = new List<Campfire>();

        [SerializeField] private CampfireConfig config;
        [Tooltip("火焰外观，燃尽后隐藏。")]
        [SerializeField] private GameObject flame;
        [Tooltip("闪烁时调节亮度的点光源。")]
        [SerializeField] private Light flameLight;
        [Tooltip("燃尽后保留余烬外观的秒数，之后销毁。")]
        [SerializeField, Min(0f)] private float emberLifetime = 6f;

        private SafeLightSource lightSource;
        private CampfireModel model;
        private float baseIntensity;
        private bool burntOut;

        /// <summary>当前正在燃烧的火堆（用于放置重叠判定和 HUD）。</summary>
        public static IReadOnlyList<Campfire> Burning => burning;
        public static event Action<Campfire> Extinguished;

        public CampfireModel Model => model;
        public float Radius => lightSource != null ? lightSource.Radius : 0f;
        public bool IsBurning => model != null && model.IsBurning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            burning.Clear();
            Extinguished = null;
        }

        private void Awake()
        {
            lightSource = GetComponent<SafeLightSource>();
            model = config != null
                ? new CampfireModel(config.burnDuration, config.warningDuration)
                : new CampfireModel(90f, 15f);
            if (config != null)
            {
                lightSource.SetRadius(config.lightRadius);
            }

            baseIntensity = flameLight != null ? flameLight.intensity : 0f;
        }

        private void OnEnable()
        {
            if (IsBurning && !burning.Contains(this)) burning.Add(this);
        }

        private void OnDisable()
        {
            burning.Remove(this);
        }

        private void Update()
        {
            if (burntOut)
            {
                return;
            }

            model.Tick(Time.deltaTime);
            if (!model.IsBurning)
            {
                BurnOut();
                return;
            }

            if (flameLight != null)
            {
                flameLight.intensity = model.IsWarning
                    ? baseIntensity * (0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.time * 9f)))
                    : baseIntensity;
            }
        }

        public bool Contains(Vector3 position)
        {
            return IsBurning && lightSource.Contains(position);
        }

        private void BurnOut()
        {
            burntOut = true;
            burning.Remove(this);
            lightSource.SetLit(false);
            if (flame != null) flame.SetActive(false);
            Extinguished?.Invoke(this);
            Destroy(gameObject, emberLifetime);
        }
    }
}
