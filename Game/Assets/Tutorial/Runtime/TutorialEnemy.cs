using UnityEngine;

namespace WildernessExtraction.Tutorial
{
    /// <summary>教学怪物的标记：死亡时记下 fact；easeable 的怪物在玩家反复倒下后会被撤走。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeleeEnemy))]
    public sealed class TutorialEnemy : MonoBehaviour
    {
        [SerializeField] private string killFact = string.Empty;
        [Tooltip("卡在原地、只能向前挥爪的失火者（近战教学）。")]
        [SerializeField] private bool pinned;
        [Tooltip("玩家在最后一段反复倒下时可以撤走。")]
        [SerializeField] private bool easeable;

        public string KillFact => killFact;
        public bool Pinned => pinned;
        public bool Easeable => easeable;
        public MeleeEnemy Enemy => GetComponent<MeleeEnemy>();
        public Health Health => GetComponent<Health>();

        public void Configure(string fact, bool isPinned, bool canEase)
        {
            killFact = fact;
            pinned = isPinned;
            easeable = canEase;
        }
    }
}
