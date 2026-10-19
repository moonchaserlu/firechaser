using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>单局规则：背包、搜索和撤离。默认值来自 VerticalSlicePlan 第 4 节。</summary>
    [CreateAssetMenu(menuName = "Wilderness/Config/Run Rules", fileName = "RunRulesConfig")]
    public sealed class RunRulesConfig : ScriptableObject
    {
        [Min(1)] public int inventoryCapacity = 16;
        [Min(0f)] public float searchDuration = 1.5f;
        [Tooltip("撤离区内需要连续停留的秒数，离开则重置。")]
        [Min(0.01f)] public float extractionDuration = 3f;

        [Tooltip("可与容器交互的水平距离（米）。策划未给定，灰盒临时值。")]
        [Min(0.1f)] public float interactRange = 2f;
    }
}
