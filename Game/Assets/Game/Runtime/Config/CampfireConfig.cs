using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>燃料与火堆规则。默认值来自《首个可玩版本计划 V0.1》第 3 节，均为试玩基线。</summary>
    [CreateAssetMenu(menuName = "Wilderness/Config/Campfire", fileName = "CampfireConfig")]
    public sealed class CampfireConfig : ScriptableObject
    {
        [Header("材料")]
        [Tooltip("建火消耗的物品（燃料包）。")]
        public ItemDefinition fuelItem;
        [Tooltip("每座火堆消耗的燃料数量。只有建造成功才扣除。")]
        [Min(1)] public int fuelCost = 1;

        [Header("放置")]
        [Tooltip("火堆中心距玩家的最大水平距离（米）。鼠标超出时预览贴在最大距离处。")]
        [Min(0.5f)] public float placeRange = 3f;
        [Tooltip("火堆占地半径（米），用于判断与阻挡体、其他火堆是否重叠。")]
        [Min(0.1f)] public float footprintRadius = 0.6f;
        [Tooltip("距安全屋光源中心小于该距离视为安全屋核心，不能建火。")]
        [Min(0f)] public float safeHouseCoreRadius = 3f;
        [Tooltip("放置点到 NavMesh 的最大偏差（米），超出视为不在可行走地面上。")]
        [Min(0.05f)] public float groundTolerance = 0.5f;
        [Tooltip("视为阻挡体的物理层。玩家自身与触发器会被忽略。")]
        public LayerMask obstacleMask = ~0;

        [Header("建造")]
        [Tooltip("确认后到点燃所需的秒数。移动、受到攻击或离开放置范围会取消且不扣燃料。")]
        [Min(0f)] public float buildDuration = 1.5f;
        [Tooltip("建造中位置偏移超过该距离（米）视为移动，取消建造。")]
        [Min(0.01f)] public float moveCancelDistance = 0.15f;
        [Tooltip("黑暗侵蚀伤害是否也取消建造。默认否：否则离光 1.5 秒预警后几乎无法在黑暗中建火。待策划确认。")]
        public bool darknessDamageCancelsBuild;

        [Header("燃烧")]
        [Tooltip("火堆的有效安全光半径（米），视觉亮区与判定边界一致。")]
        [Min(0.5f)] public float lightRadius = 6f;
        [Tooltip("点燃后持续的秒数。本版不能添柴。")]
        [Min(1f)] public float burnDuration = 90f;
        [Tooltip("剩余秒数低于该值时明显闪烁，提示即将熄灭。")]
        [Min(0f)] public float warningDuration = 15f;
    }
}
