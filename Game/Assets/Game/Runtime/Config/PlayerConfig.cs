using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>玩家生命、移动、奔跑、翻滚和体力数值。默认值来自 VerticalSlicePlan 第 4 节；奔跑与移速倍率为参考鸭科夫的占位值。</summary>
    [CreateAssetMenu(menuName = "Wilderness/Config/Player", fileName = "PlayerConfig")]
    public sealed class PlayerConfig : ScriptableObject
    {
        [Header("生命")]
        [Min(1f)] public float maxHealth = 100f;

        [Header("移动 (m/s)")]
        [Min(0f)] public float moveSpeed = 5f;

        [Header("奔跑（Shift）")]
        [Min(0f)] public float sprintSpeed = 7.5f;
        [Tooltip("奔跑时每秒消耗的体力；体力不足时自动变回走路，松开 Shift 后才能再次奔跑。")]
        [Min(0f)] public float sprintStaminaPerSecond = 15f;

        [Header("移速倍率")]
        [Tooltip("按住右键精确瞄准时。")]
        [Range(0.1f, 1f)] public float aimMoveMultiplier = 0.6f;
        [Tooltip("收起武器（空手）时，对走路和奔跑都生效。")]
        [Range(1f, 2f)] public float holsteredMoveMultiplier = 1.15f;
        [Tooltip("使用物品读条期间。")]
        [Range(0.1f, 1f)] public float itemUseMoveMultiplier = 0.5f;

        [Header("翻滚")]
        [Min(0.01f)] public float dodgeDuration = 0.35f;
        [Min(0f)] public float dodgeSpeed = 10f;
        [Min(0f)] public float dodgeStaminaCost = 25f;

        [Header("体力")]
        [Min(1f)] public float maxStamina = 100f;
        [Min(0f)] public float staminaRegenPerSecond = 20f;
        [Tooltip("最后一次消耗体力后，经过该秒数才开始恢复。")]
        [Min(0f)] public float staminaRegenDelay = 0.5f;
    }
}
