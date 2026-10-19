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
        [Min(0f)] public float alertRadius = 8f;

        [Header("灰盒临时值（策划未给定）")]
        [Tooltip("进入该水平距离后开始攻击（米）。")]
        [Min(0.1f)] public float attackRange = 1.6f;
        [Tooltip("进入攻击状态后第一次出手前的前摇（秒）。")]
        [Min(0f)] public float attackWindup = 0.35f;
        [Tooltip("发现玩家后到开始追逐的反应时间（秒）。")]
        [Min(0f)] public float reactionTime = 0.4f;
        [Min(0f)] public float moveSpeed = 3.5f;
    }
}
