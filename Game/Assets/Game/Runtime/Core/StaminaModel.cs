using System;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>体力：一次性消耗，最后一次消耗后经过延迟才按速率恢复。</summary>
    public sealed class StaminaModel
    {
        private readonly float regenPerSecond;
        private readonly float regenDelay;
        private float sinceLastConsume;

        public StaminaModel(float max, float regenPerSecond, float regenDelay)
        {
            if (max <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(max));
            }

            Max = max;
            Current = max;
            this.regenPerSecond = Mathf.Max(0f, regenPerSecond);
            this.regenDelay = Mathf.Max(0f, regenDelay);
            sinceLastConsume = this.regenDelay;
        }

        public float Max { get; }
        public float Current { get; private set; }
        public float Normalized => Current / Max;

        public bool CanAfford(float cost) => Current >= cost;

        public bool TryConsume(float cost)
        {
            if (cost < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cost));
            }

            if (Current < cost)
            {
                return false;
            }

            Current -= cost;
            sinceLastConsume = 0f;
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            var regenTime = deltaTime;
            if (sinceLastConsume < regenDelay)
            {
                var waited = Mathf.Min(deltaTime, regenDelay - sinceLastConsume);
                regenTime -= waited;
            }

            sinceLastConsume += deltaTime;
            if (regenTime > 0f)
            {
                Current = Mathf.Min(Max, Current + regenPerSecond * regenTime);
            }
        }
    }
}
