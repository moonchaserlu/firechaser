using UnityEngine;

namespace WildernessExtraction
{
    public enum LightExposureState
    {
        /// <summary>处于任一有效光源范围内。</summary>
        Safe,
        /// <summary>刚离开光源，预警中，不扣血。</summary>
        Warning,
        /// <summary>预警结束，持续受到黑暗伤害。</summary>
        Exposed,
    }

    /// <summary>
    /// 黑暗暴露计时：离光后先预警，预警结束后按秒扣血；回到光内立即清零。
    /// 只计算伤害，不直接修改生命，便于 EditMode 测试。
    /// </summary>
    public sealed class LightExposureModel
    {
        public LightExposureModel(float warningDuration, float damagePerSecond)
        {
            WarningDuration = Mathf.Max(0f, warningDuration);
            DamagePerSecond = Mathf.Max(0f, damagePerSecond);
        }

        public float WarningDuration { get; }
        public float DamagePerSecond { get; }
        public LightExposureState State { get; private set; } = LightExposureState.Safe;

        /// <summary>本次离开光源后经过的秒数。</summary>
        public float DarkTime { get; private set; }

        public float WarningRemaining => Mathf.Max(0f, WarningDuration - DarkTime);

        /// <summary>推进计时，返回本帧应造成的黑暗伤害。</summary>
        public float Tick(bool inLight, float deltaTime)
        {
            if (inLight)
            {
                Reset();
                return 0f;
            }

            if (deltaTime <= 0f)
            {
                return 0f;
            }

            var before = DarkTime;
            DarkTime += deltaTime;
            State = DarkTime >= WarningDuration ? LightExposureState.Exposed : LightExposureState.Warning;

            // 同一帧跨过预警终点时，只对超出部分计伤。
            var exposedSeconds = DarkTime - Mathf.Max(before, WarningDuration);
            return exposedSeconds > 0f ? exposedSeconds * DamagePerSecond : 0f;
        }

        public void Reset()
        {
            DarkTime = 0f;
            State = LightExposureState.Safe;
        }
    }
}
