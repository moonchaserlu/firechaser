using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>流血、血迹与怪物循迹规则。默认值来自《首个可玩版本计划 V0.1》第 3 节，均为试玩基线。</summary>
    [CreateAssetMenu(menuName = "Wilderness/Config/Bleeding", fileName = "BleedingConfig")]
    public sealed class BleedingConfig : ScriptableObject
    {
        [Header("流血")]
        [Tooltip("近战命中后的流血秒数。重复命中只刷新时长，不叠层。")]
        [Min(0.1f)] public float bleedDuration = 8f;
        [Tooltip("流血且移动时，每隔多少秒留下一处血迹。")]
        [Min(0.1f)] public float dropInterval = 1f;
        [Tooltip("最近该秒数内发生过位移即视为移动中（平滑逐帧判定）。")]
        [Min(0.01f)] public float movingGrace = 0.25f;

        [Header("血迹")]
        [Tooltip("单处血迹存在的秒数。")]
        [Min(0.1f)] public float markLifetime = 15f;
        [Tooltip("全场最多保留的血迹数量，超出时移除最旧的一处。")]
        [Min(1)] public int maxMarks = 12;

        [Header("循迹")]
        [Tooltip("未发现玩家的怪物能察觉血迹的水平距离（米）。处于有效光内的血迹气味被遮蔽。")]
        [Min(0f)] public float scentRange = 12f;
        [Tooltip("循迹怪物连续该秒数既没看到玩家、也没找到更新的血迹，就放弃调查。")]
        [Min(0.1f)] public float giveUpWithoutSight = 3f;
    }
}
