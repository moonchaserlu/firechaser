using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    public enum ContainerType { CivilianCrate, MedicalCabinet, AmmoBox, Toolbox, LockedSafe, Corpse }
    /// <summary>
    /// 可搜索容器。首次交互需要搜索，完成后按固定掉落表生成物品；
    /// 背包放不下的物品留在容器中，之后可再次按 E 直接拾取。
    /// </summary>
    public sealed class LootContainer : MonoBehaviour
    {
        private static readonly List<LootContainer> active = new List<LootContainer>();

        [SerializeField] private string displayName = "容器";
        [SerializeField] private ContainerType containerType;
        [SerializeField] private LootTable lootTable;
        [SerializeField, Min(0f)] private float searchDuration = 1.5f;
        [SerializeField] private int seedOffset;
        [Tooltip("玩家专用（如近郊燃料保底箱）：AI Bot 不会搜刮，避免玩家被软锁。")]
        [SerializeField] private bool reservedForPlayer;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Color emptyColor = new Color(0.25f, 0.25f, 0.25f);

        private LootPile pile;
        private List<ItemDefinition> presetItems;

        public static IReadOnlyList<LootContainer> Active => active;

        public string DisplayName => displayName;
        public LootTable LootTable => lootTable;
        public ContainerType Type => containerType;
        public bool ReservedForPlayer => reservedForPlayer;
        public float SearchDuration => searchDuration;
        public bool IsSearched => pile != null;
        public bool IsEmpty => pile != null && pile.IsEmpty;
        public bool IsInteractable => !IsEmpty;
        public IReadOnlyList<ItemDefinition> Remaining => pile != null ? pile.Remaining : Array.Empty<ItemDefinition>();

        private void OnEnable()
        {
            active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
        }

        /// <summary>运行时把对象变成固定内容的可搜索容器（例如人机尸体）。仍需完整搜索时间。</summary>
        public void InitializeWithItems(string containerName, ContainerType type, float duration, IEnumerable<ItemDefinition> items)
        {
            displayName = containerName;
            containerType = type;
            searchDuration = duration;
            lootTable = null;
            pile = null;
            presetItems = new List<ItemDefinition>(items);
        }

        public float HorizontalDistanceTo(Vector3 position)
        {
            var delta = position - transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        public void MarkSearched()
        {
            if (IsSearched)
            {
                return;
            }

            if (presetItems != null)
            {
                pile = new LootPile(presetItems);
                presetItems = null;
                RefreshVisual();
                return;
            }

            var seed = RunLootSeed.Current + seedOffset;
            pile = new LootPile(lootTable != null ? lootTable.CreateDrops(new System.Random(seed)) : new List<ItemDefinition>());
            RefreshVisual();
        }

        public TransferResult TransferTo(Inventory inventory)
        {
            if (!IsSearched)
            {
                throw new InvalidOperationException($"{name} 尚未搜索完成。");
            }

            var result = pile.TransferTo(inventory);
            RefreshVisual();
            return result;
        }

        public static LootContainer FindNearestInteractable(Vector3 position, float range)
        {
            LootContainer nearest = null;
            var nearestDistance = range;
            foreach (var container in active)
            {
                if (!container.IsInteractable)
                {
                    continue;
                }

                var distance = container.HorizontalDistanceTo(position);
                if (distance <= nearestDistance)
                {
                    nearest = container;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private void RefreshVisual()
        {
            if (bodyRenderer != null && IsEmpty)
            {
                bodyRenderer.material.color = emptyColor;
            }
        }
    }

    /// <summary>每局一个可记录的掉落种子；场景重载会产生新种子。</summary>
    public static class RunLootSeed
    {
        public static int Current { get; private set; } = Environment.TickCount;
        public static void Set(int seed) => Current = seed;
    }
}
