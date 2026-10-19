using System;
using UnityEngine;

namespace WildernessExtraction
{
    public enum SearchCancelReason
    {
        OutOfRange,
        Damaged,
        Interrupted
    }

    /// <summary>
    /// 按 E 搜索附近容器。搜索中离开交互范围或受到伤害会取消；
    /// 背包已满时剩余物品留在容器中并发出提示事件。
    /// </summary>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private RunRulesConfig rules;
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private Health health;

        private readonly TimedProgress search = new TimedProgress();
        private LootContainer searchTarget;

        public event Action<LootContainer> SearchStarted;
        public event Action<LootContainer, SearchCancelReason> SearchCancelled;
        public event Action<LootContainer, TransferResult> ItemsCollected;
        /// <summary>背包已满，物品留在容器中。</summary>
        public event Action<LootContainer> InventoryFull;

        /// <summary>当前可交互的最近容器（供 HUD 提示）。</summary>
        public LootContainer Focus { get; private set; }
        public LootContainer SearchTarget => searchTarget;
        public bool IsSearching => searchTarget != null;
        public float SearchProgress => search.Progress;
        public float SearchDuration => searchTarget != null ? searchTarget.SearchDuration : rules.searchDuration;

        private void Awake()
        {
            if (input == null)
            {
                input = GetComponent<PlayerInputReader>();
            }

            if (inventory == null)
            {
                inventory = GetComponent<PlayerInventory>();
            }

            if (health == null)
            {
                health = GetComponent<Health>();
            }
        }

        private void OnEnable()
        {
            health.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            health.Damaged -= OnDamaged;
            CancelSearch(SearchCancelReason.Interrupted);
            Focus = null;
        }

        private void Update()
        {
            Focus = LootContainer.FindNearestInteractable(transform.position, rules.interactRange);

            if (IsSearching)
            {
                if (searchTarget.HorizontalDistanceTo(transform.position) > rules.interactRange)
                {
                    CancelSearch(SearchCancelReason.OutOfRange);
                }
                else if (search.Tick(Time.deltaTime))
                {
                    CompleteSearch();
                }

                return;
            }

            if (input.InteractPressed)
            {
                TryInteract();
            }
        }

        /// <summary>与范围内最近的容器交互：未搜索则开始搜索，已搜索则拾取剩余物品。</summary>
        public bool TryInteract()
        {
            if (IsSearching || !isActiveAndEnabled)
            {
                return false;
            }

            var target = LootContainer.FindNearestInteractable(transform.position, rules.interactRange);
            if (target == null)
            {
                return false;
            }

            if (target.IsSearched)
            {
                Collect(target);
                return true;
            }

            searchTarget = target;
            search.Start(target.SearchDuration > 0f ? target.SearchDuration : rules.searchDuration);
            SearchStarted?.Invoke(target);
            return true;
        }

        public void CancelSearch(SearchCancelReason reason)
        {
            if (!IsSearching)
            {
                return;
            }

            var target = searchTarget;
            searchTarget = null;
            search.Cancel();
            SearchCancelled?.Invoke(target, reason);
        }

        private void OnDamaged(float amount)
        {
            CancelSearch(SearchCancelReason.Damaged);
        }

        private void CompleteSearch()
        {
            var target = searchTarget;
            searchTarget = null;
            target.MarkSearched();
            Collect(target);
        }

        private void Collect(LootContainer target)
        {
            var result = target.TransferTo(inventory.Inventory);
            ItemsCollected?.Invoke(target, result);
            if (result.BlockedByFullInventory)
            {
                InventoryFull?.Invoke(target);
            }
        }
    }
}
