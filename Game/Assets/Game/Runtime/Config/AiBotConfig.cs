using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 敌对人机（类似塔科夫 Scav）参数：对玩家敌对、与怪物互相敌对；在自己的地盘巡逻搜刮，不使用玩家的安全屋与撤离点。
    /// 与玩家遵守相同的黑暗、背包、搜索时间和枪械规则。数值均为试玩基线。
    /// </summary>
    [CreateAssetMenu(menuName = "Wilderness/Config/AI Bot", fileName = "AiBotConfig")]
    public sealed class AiBotConfig : ScriptableObject
    {
        [Header("基础")]
        [Min(1f)] public float maxHealth = 100f;
        [Tooltip("移动速度（米/秒），略慢于玩家。")]
        [Min(0.5f)] public float moveSpeed = 4.2f;
        [Tooltip("感知与决策刷新间隔（秒），不必每帧执行。")]
        [Min(0.05f)] public float thinkInterval = 0.25f;

        [Header("视觉索敌（与怪物同一套规则：距离、视锥、遮挡）")]
        [Tooltip("正面视野距离（米）。持枪人机看得比怪物远。")]
        [Min(1f)] public float sightRange = 14f;
        [Range(1f, 360f)] public float fieldOfView = 120f;
        [Tooltip("近身感知半径（米），背后贴近也会被发现。")]
        [Min(0f)] public float proximityRadius = 2f;
        [Tooltip("目标需连续可见该秒数才算发现。")]
        [Min(0f)] public float detectionConfirmTime = 0.5f;
        [Tooltip("交战中保持视认的最大距离（米），不受视锥限制，仍受墙体遮挡。")]
        [Min(1f)] public float combatSightRange = 18f;
        [Tooltip("丢失目标后去最后目击点搜寻的秒数。")]
        [Min(0f)] public float targetMemoryDuration = 4f;
        [Min(0f)] public float eyeHeight = 1.6f;
        public LayerMask sightMask = ~0;

        [Header("枪械（使用与玩家相同的 WeaponConfig 与弹药物品）")]
        [Tooltip("开局携带的备弹数量（背包内的弹药物品）。")]
        [Min(0)] public int startingAmmo = 24;
        [Tooltip("发现目标后第一次开枪前的瞄准时间（秒）。")]
        [Min(0f)] public float aimDelay = 0.45f;
        [Tooltip("散布倍率：人机枪法比玩家差，避免显得作弊。")]
        [Min(1f)] public float spreadMultiplier = 1.8f;
        [Tooltip("理想交战距离（米）：远于此会靠近，打怪物时近于“保持距离”会后撤。")]
        [Min(1f)] public float preferredRange = 10f;
        [Tooltip("与怪物保持的最小距离（米）。")]
        [Min(0f)] public float keepAwayFromMonster = 5f;

        [Header("地盘与搜刮")]
        [Tooltip("以出生点为中心的活动半径（米）。只在地盘内巡逻、搜刮与追击，不会走向玩家的安全屋。")]
        [Min(2f)] public float territoryRadius = 20f;
        [Tooltip("在一处光源停留观察的秒数，之后换到地盘内另一处光源。")]
        [Min(0f)] public float postWaitDuration = 6f;
        [Tooltip("与容器交互的水平距离（米）。")]
        [Min(0.5f)] public float interactRange = 2f;
        [Tooltip("日常巡逻、搜刮、听声调查时允许承受的黑暗伤害上限。0 = 只走不掉血的光路（黑暗间隔在预警期内）。")]
        [Min(0f)] public float routeDamageBudget = 0f;
        [Tooltip("每点预计黑暗伤害折算的路程代价（米）。")]
        [Min(0f)] public float damageWeight = 4f;

        [Header("撤退")]
        [Tooltip("生命低于最大生命的该比例时，撤回地盘内最近的光区（仍会还击）。")]
        [Range(0f, 1f)] public float retreatHealthFraction = 0.35f;

        [Header("调试")]
        [Tooltip("头顶显示当前状态与目标（开发模式）。")]
        public bool showDebugLabel = true;
    }
}
