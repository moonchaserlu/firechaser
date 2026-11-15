using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>单局规则：背包、搜索和撤离。默认值来自 VerticalSlicePlan 第 4 节。</summary>
    [CreateAssetMenu(menuName = "Wilderness/Config/Run Rules", fileName = "RunRulesConfig")]
    public sealed class RunRulesConfig : ScriptableObject
    {
        [Min(1)] public int inventoryCapacity = 16;
        [Min(0f)] public float searchDuration = 1.5f;
        [Tooltip("安全屋入口返程区内需要连续停留的秒数，离开或受伤则重置。光路原型为 2 秒。")]
        [Min(0.01f)] public float extractionDuration = 2f;
        [Tooltip("玩家离开安全屋入口超过该水平距离（米）后，返程区才启用，防止出生即结算。")]
        [Min(0f)] public float returnArmDistance = 9f;

        [Tooltip("可与容器交互的水平距离（米）。策划未给定，灰盒临时值。")]
        [Min(0.1f)] public float interactRange = 2f;
    }
}
