using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    public enum ContainerType { CivilianCrate, MedicalCabinet, AmmoBox, Toolbox, LockedSafe, Corpse }
    /// <summary>
    /// 可搜索容器。第一次打开时按掉落表生成物品（玩家看不见），搜索时逐件揭示；
    /// 揭示的物品留在容器格子里，玩家拖进背包或快捷转移才算拿走。AI 人机仍是整箱搜完后全部拿走。
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

        private ContainerContents contents;
        private List<ItemDefinition> presetItems;

        public static IReadOnlyList<LootContainer> Active => active;

        public string DisplayName => displayName;
        public LootTable LootTable => lootTable;
        public ContainerType Type => containerType;
        public bool ReservedForPlayer => reservedForPlayer;
        public float SearchDuration => searchDuration;
        /// <summary>已经打开过（物品已生成），不一定全部揭示。</summary>
        public bool IsOpened => contents != null;
        /// <summary>所有物品都已揭示（可能还有物品没拿走）。</summary>
        public bool IsSearched => contents != null && contents.AllRevealed;
        /// <summary>搜完且拿空。</summary>
        public bool IsEmpty => IsSearched && contents.IsEmpty;
        public bool IsInteractable => !IsEmpty;
        /// <summary>容器格子；未打开时为 null。</summary>
        public ContainerContents Contents => contents;
        /// <summary>容器里还没拿走的物品（含未揭示的）；未打开时为空。</summary>
        public IReadOnlyList<ItemDefinition> Remaining => contents != null ? contents.Items : Array.Empty<ItemDefinition>();
        /// <summary>容器里的物品发生变化（揭示、拿走）。</summary>
        public event Action Changed;

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
            SetContents(null);
            presetItems = new List<ItemDefinition>(items);
        }

        public float HorizontalDistanceTo(Vector3 position)
        {
            var delta = position - transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        /// <summary>第一次打开：生成物品（全部未揭示）。已打开时不变。</summary>
        public ContainerContents Open()
        {
            if (contents != null)
            {
                return contents;
            }

            if (presetItems != null)
            {
                SetContents(new ContainerContents(presetItems));
                presetItems = null;
            }
            else
            {
                var seed = RunLootSeed.Current + seedOffset;
                SetContents(new ContainerContents(lootTable != null ? lootTable.CreateDrops(new System.Random(seed)) : new List<ItemDefinition>()));
            }

            return contents;
        }

        /// <summary>一次揭示全部物品（AI 人机、测试用）。</summary>
        public void MarkSearched()
        {
            Open().RevealAll();
        }

        /// <summary>下一件待揭示物品的搜索秒数；没有待揭示物品时为 0。</summary>
        public float NextItemSearchSeconds()
        {
            var opened = Open();
            var index = opened.NextHidden;
            return index < 0 ? 0f : ContainerContents.SearchSeconds(opened[index], searchDuration);
        }

        /// <summary>拿走全部已揭示物品，放不下的留下。</summary>
        public TransferResult TransferTo(Inventory inventory)
        {
            if (!IsOpened)
            {
                throw new InvalidOperationException($"{name} 尚未搜索。");
            }

            return contents.TakeAllRevealed(inventory);
        }

        private void SetContents(ContainerContents value)
        {
            if (contents != null) contents.Changed -= OnContentsChanged;
            contents = value;
            if (contents != null) contents.Changed += OnContentsChanged;
            OnContentsChanged();
        }

        private void OnContentsChanged()
        {
            RefreshVisual();
            Changed?.Invoke();
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
