using System;
using UnityEngine;

namespace WildernessExtraction.Tutorial
{
    /// <summary>内容固定的教学箱子：开局把物品交给同物体上的 LootContainer；搜完拿走后教学导演记下 fact。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LootContainer))]
    public sealed class TutorialCrate : MonoBehaviour
    {
        [SerializeField] private string fact = string.Empty;
        [SerializeField] private string crateName = "货箱";
        [SerializeField, Min(0f)] private float searchDuration = 1.2f;
        [SerializeField] private ItemDefinition[] items = Array.Empty<ItemDefinition>();

        public string Fact => fact;

        public void Configure(string crateFact, string displayName, float duration, ItemDefinition[] contents)
        {
            fact = crateFact;
            crateName = displayName;
            searchDuration = duration;
            items = contents ?? Array.Empty<ItemDefinition>();
        }

        private void Awake()
        {
            GetComponent<LootContainer>().InitializeWithItems(crateName, ContainerType.CivilianCrate, searchDuration, items);
        }
    }
}
