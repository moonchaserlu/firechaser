using System;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>可复用的生命组件。死亡事件只触发一次，死亡后忽略后续伤害。</summary>
    [DisallowMultipleComponent]
    public sealed class Health : MonoBehaviour
    {
        [Tooltip("由拥有者的配置覆盖（PlayerConfig / EnemyConfig）。")]
        [SerializeField, Min(1f)] private float maxHealth = 100f;

        private HealthModel model;

        /// <summary>参数为实际扣除的生命值。</summary>
        public event Action<float> Damaged;
        public event Action Died;

        public float Current => Model.Current;
        public float Max => Model.Max;
        public float Normalized => Model.Normalized;
        public bool IsDead => Model.IsDead;

        private HealthModel Model => model ??= new HealthModel(maxHealth);

        public void Configure(float max)
        {
            maxHealth = max;
            model = new HealthModel(max);
        }

        /// <summary>返回是否实际造成了伤害。</summary>
        public bool TakeDamage(float amount)
        {
            if (Model.IsDead)
            {
                return false;
            }

            var applied = Model.ApplyDamage(amount);
            if (applied <= 0f)
            {
                return false;
            }

            Damaged?.Invoke(applied);
            if (Model.IsDead)
            {
                Died?.Invoke();
            }

            return true;
        }
    }
}
