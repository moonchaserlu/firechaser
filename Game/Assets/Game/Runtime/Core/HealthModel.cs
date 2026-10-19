using System;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>生命值。死亡后不再接受伤害，保证死亡只发生一次。</summary>
    public sealed class HealthModel
    {
        public HealthModel(float max)
        {
            if (max <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(max));
            }

            Max = max;
            Current = max;
        }

        public float Max { get; }
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;
        public float Normalized => Current / Max;

        /// <summary>返回实际扣除的生命值；已死亡或伤害非正时返回 0。</summary>
        public float ApplyDamage(float amount)
        {
            if (IsDead || amount <= 0f)
            {
                return 0f;
            }

            var applied = Mathf.Min(amount, Current);
            Current -= applied;
            return applied;
        }
    }
}
