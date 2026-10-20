using System;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>玩家黑暗暴露：每帧查询有效光源，离光后按 DarknessConfig 预警并通过 Health 扣血。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public sealed class LightExposure : MonoBehaviour
    {
        [SerializeField] private DarknessConfig config;

        private Health health;
        private LightExposureModel model;

        public event Action<LightExposureState> StateChanged;

        public LightExposureState State => Model.State;
        public float WarningRemaining => Model.WarningRemaining;
        public float DamagePerSecond => Model.DamagePerSecond;

        private LightExposureModel Model => model ??= config != null
            ? new LightExposureModel(config.warningDuration, config.damagePerSecond)
            : new LightExposureModel(1.5f, 20f);

        private void Awake()
        {
            health = GetComponent<Health>();
            if (config == null)
            {
                Debug.LogWarning("[Darkness] LightExposure 未设置 DarknessConfig，使用默认规则。", this);
            }
        }

        private void OnDisable()
        {
            SetSafe();
        }

        private void Update()
        {
            if (health.IsDead)
            {
                return;
            }

            var previous = Model.State;
            var damage = Model.Tick(LightRegistry.Scene.IsLit(transform.position), Time.deltaTime);
            if (Model.State != previous)
            {
                StateChanged?.Invoke(Model.State);
            }

            if (damage > 0f)
            {
                health.TakeDamage(damage);
            }
        }

        private void SetSafe()
        {
            if (Model.State == LightExposureState.Safe)
            {
                return;
            }

            Model.Reset();
            StateChanged?.Invoke(LightExposureState.Safe);
        }
    }
}
