using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>近战敌人数值。生命/伤害/攻击间隔/警戒半径来自 VerticalSlicePlan 第 4 节，其余为灰盒临时值。</summary>
    [CreateAssetMenu(menuName = "Wilderness/Config/Enemy", fileName = "EnemyConfig")]
    public sealed class EnemyConfig : ScriptableObject
    {
        [Min(1f)] public float maxHealth = 75f;
        [Min(0f)] public float damage = 20f;
        [Min(0.05f)] public float attackInterval = 1f;
        [Tooltip("正面视野距离（米）。策划案《敌人与 AI Bot 行为》建议 9 米。")]
        [Min(0f)] public float alertRadius = 9f;

        [Header("灰盒临时值（策划未给定）")]
        [Tooltip("进入该水平距离后开始攻击（米）。")]
        [Min(0.1f)] public float attackRange = 1.6f;
        [Tooltip("进入攻击状态后第一次出手前的前摇（秒）。")]
        [Min(0f)] public float attackWindup = 0.35f;
        [Tooltip("发现玩家后到开始追逐的反应时间（秒）。")]
        [Min(0f)] public float reactionTime = 0.4f;
        [Min(0f)] public float moveSpeed = 3.5f;

        [Header("视觉索敌（策划案 4.3 初值）")]
        [Tooltip("正面视锥角度（度）。背后不会被直接发现。")]
        [Range(1f, 360f)] public float fieldOfView = 110f;
        [Tooltip("近身感知半径（米）：防止贴在怪物背后完全不被发现，范围必须很小。仍需无墙体遮挡。")]
        [Min(0f)] public float proximityRadius = 1.5f;
        [Tooltip("目标需连续可见该秒数才算发现（之后进入短暂警觉 reactionTime）。")]
        [Min(0f)] public float detectionConfirmTime = 0.35f;
        [Tooltip("追击中保持视认的最大距离（米）。追击时不再受视锥限制，但仍受墙体遮挡。")]
        [Min(0f)] public float chaseSightRange = 12f;
        [Tooltip("丢失视认后仍记得目标、前往最后目击点的秒数；期间重新看见则继续追击。")]
        [Min(0f)] public float targetMemoryDuration = 3f;
        [Tooltip("到达最后目击点后原地环顾搜索的秒数，之后返回巡逻。")]
        [Min(0f)] public float searchDuration = 4f;
        [Tooltip("视线起点高度（米）。矮掩体（1.2 米）不遮挡视线，墙体（2 米）遮挡。")]
        [Min(0f)] public float eyeHeight = 1.5f;
        [Tooltip("遮挡视线的物理层。")]
        public LayerMask sightMask = ~0;
        [Tooltip("感知刷新间隔（秒），不必每帧执行。")]
        [Min(0.02f)] public float perceptionInterval = 0.2f;

        [Header("巡逻（策划案 4.2）")]
        [Tooltip("到达巡逻点后的停留秒数（策划案 1–2 秒）。")]
        [Min(0f)] public float patrolWaitDuration = 1.5f;
        [Tooltip("巡逻移动速度相对追击速度的比例。")]
        [Range(0.1f, 1f)] public float patrolSpeedMultiplier = 0.6f;
        [Tooltip("前往巡逻点时超过该秒数没有明显进展视为卡住，改去下一个点。")]
        [Min(0.5f)] public float patrolStuckTimeout = 4f;
    }
}
