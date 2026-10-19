using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>玩家生命、移动、翻滚和体力数值。默认值来自 VerticalSlicePlan 第 4 节。</summary>
    [CreateAssetMenu(menuName = "Wilderness/Config/Player", fileName = "PlayerConfig")]
    public sealed class PlayerConfig : ScriptableObject
    {
        [Header("生命")]
        [Min(1f)] public float maxHealth = 100f;

        [Header("移动 (m/s)")]
        [Min(0f)] public float moveSpeed = 5f;

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
