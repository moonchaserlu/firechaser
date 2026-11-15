using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>永久黑暗规则：离光预警、黑暗伤害和固定光源半径。默认值来自《首个可玩版本计划 V0.1》第 3 节，均为试玩基线。</summary>
    [CreateAssetMenu(menuName = "Wilderness/Config/Darkness", fileName = "DarknessConfig")]
    public sealed class DarknessConfig : ScriptableObject
    {
        [Header("黑暗暴露")]
        [Tooltip("离开有效光源后的预警秒数，预警期不扣血。给玩家一次回头或冲刺的机会。")]
        [Min(0f)] public float warningDuration = 1.5f;

        [Tooltip("预警结束后每秒黑暗伤害。生命 100 时满血约 6.5 秒死亡（含预警）。")]
        [Min(0f)] public float damagePerSecond = 20f;

        [Header("有效光源半径（米）")]
        [Tooltip("安全屋常亮光的玩法半径。")]
        [Min(0.5f)] public float safeHouseRadius = 6f;

        [Tooltip("地图固定安全灯的玩法半径。固定灯只覆盖部分落脚点，不应连成直达深区的安全链。")]
        [Min(0.5f)] public float fixedLampRadius = 4f;

        [Tooltip("火把的玩法半径，应小于固定灯。火把是落脚点的点缀，不是路灯。")]
        [Min(0.5f)] public float torchRadius = 4f;

        [Header("视觉")]
        [Tooltip("点光源照射距离 = 玩法半径 × 该倍数。略大于 1，让亮区在判定边界处明显衰减。")]
        [Min(1f)] public float lightRangeMultiplier = 1.2f;

        public float RadiusFor(SafeLightKind kind)
        {
            return kind == SafeLightKind.SafeHouse ? safeHouseRadius : fixedLampRadius;
        }
    }
}
