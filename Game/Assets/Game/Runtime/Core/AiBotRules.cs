using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>敌对人机的纯规则（射击衰减、散布、容器偏好），便于 EditMode 测试。</summary>
    public static class AiBotRules
    {
        /// <summary>与玩家射击相同的距离衰减：有效射程内满伤，之后线性降到最低倍率。</summary>
        public static float DamageFalloff(float distance, float effectiveRange, float range, float minimumMultiplier)
        {
            if (distance <= effectiveRange) return 1f;
            return Mathf.Lerp(1f, minimumMultiplier, Mathf.InverseLerp(effectiveRange, range, distance));
        }

        /// <summary>人机散布（度）：在玩家站立/移动散布的基础上乘以倍率。</summary>
        public static float Spread(float standingSpread, float movingSpread, bool moving, float multiplier)
        {
            return (moving ? movingSpread : standingSpread) * Mathf.Max(1f, multiplier);
        }

        /// <summary>按类型给容器打分（人机不知道具体掉落）。</summary>
        public static float ContainerTypeScore(ContainerType type)
        {
            switch (type)
            {
                case ContainerType.LockedSafe: return 70f;
                case ContainerType.AmmoBox: return 50f;
                case ContainerType.Corpse: return 40f;
                case ContainerType.MedicalCabinet: return 35f;
                case ContainerType.Toolbox: return 35f;
                default: return 30f;
            }
        }
    }
}
