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
    /// 按 F（E 备用）打开附近容器：打开后操作关闭、世界不停，容器里的物品按顺序逐件搜出（越稀有越久），
    /// 搜出的物品留在容器格子里，由 <see cref="ContainerLootPanel"/> 拖进背包或快捷转移（<see cref="TakeAt"/> / <see cref="TakeAll"/>）。
    /// F / Tab / Esc 关闭；离开交互范围或受到伤害会关闭面板，已搜出的物品保留，正在搜的那件下次重新计时。
    /// </summary>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private RunRulesConfig rules;
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private Health health;

        private readonly TimedProgress search = new TimedProgress();
        private LootContainer lootTarget;
        private int openedFrame = -1;
        private int closedFrame = -1;
        private bool disabledGameplay;

        /// <summary>打开了一个容器（不一定在搜索中）。</summary>
        public event Action<LootContainer> SearchStarted;
        public event Action<LootContainer, SearchCancelReason> SearchCancelled;
        /// <summary>搜出了一件物品（容器、格子下标）。</summary>
        public event Action<LootContainer, int> ItemRevealed;
        /// <summary>容器面板关闭。</summary>
        public event Action<LootContainer> LootClosed;
        /// <summary>从容器拿走了物品（拖拽、快捷转移或全部拿取）。</summary>
        public event Action<LootContainer, TransferResult> ItemsCollected;
        /// <summary>背包已满，物品留在容器中。</summary>
        public event Action<LootContainer> InventoryFull;

        /// <summary>当前可交互的最近容器（供 HUD 提示）。</summary>
        public LootContainer Focus { get; private set; }
        /// <summary>打开着的容器；没打开时为 null。</summary>
        public LootContainer LootTarget => lootTarget;
        public bool IsLooting => lootTarget != null;
        /// <summary>正在搜某一件物品。</summary>
        public bool IsSearching => lootTarget != null && search.IsRunning;
        public LootContainer SearchTarget => IsSearching ? lootTarget : null;
        /// <summary>当前这件物品的搜索进度 0–1。</summary>
        public float SearchProgress => search.Progress;
        /// <summary>当前这件物品的搜索秒数。</summary>
        public float SearchDuration => IsSearching ? search.Duration : rules != null ? rules.searchDuration : 0f;
        /// <summary>正在搜的格子下标；没在搜为 -1。</summary>
        public int SearchingIndex => IsSearching && lootTarget.Contents != null ? lootTarget.Contents.NextHidden : -1;
        public Inventory Bag => inventory.Inventory;

        /// <summary>容器面板打开中或本帧刚关上：Tab / Esc 已被面板用掉，背包和暂停不要再响应。</summary>
        public bool ConsumesMenuInputThisFrame => IsLooting || closedFrame == Time.frameCount;

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
            CloseLoot(SearchCancelReason.Interrupted);
            Focus = null;
        }

        private void Update()
        {
            Focus = LootContainer.FindNearestInteractable(transform.position, rules.interactRange);

            if (IsLooting)
            {
                if (lootTarget == null || !lootTarget.isActiveAndEnabled || lootTarget.HorizontalDistanceTo(transform.position) > rules.interactRange)
                {
                    CloseLoot(SearchCancelReason.OutOfRange);
                    return;
                }

                // 打开的那一下 F 不算关闭。
                if (Time.frameCount > openedFrame && (input.ConfirmPressed || input.InventoryPressed || input.PausePressed))
                {
                    CloseLoot(SearchCancelReason.Interrupted);
                    return;
                }

                if (search.Tick(Time.deltaTime))
                {
                    var target = lootTarget;
                    var index = target.Contents.RevealNext();
                    if (index >= 0) ItemRevealed?.Invoke(target, index);
                    StartNextItem();
                }

                return;
            }

            if (input.InteractPressed && closedFrame != Time.frameCount)
            {
                TryInteract();
            }
        }

        /// <summary>打开范围内最近的容器，并从第一件没搜过的物品开始搜。</summary>
        public bool TryInteract()
        {
            if (IsLooting || !isActiveAndEnabled)
            {
                return false;
            }

            var target = LootContainer.FindNearestInteractable(transform.position, rules.interactRange);
            if (target == null)
            {
                return false;
            }

            target.Open();
            lootTarget = target;
            openedFrame = Time.frameCount;
            if (input.GameplayEnabled)
            {
                input.SetGameplayEnabled(false);
                disabledGameplay = true;
            }

            StartNextItem();
            SearchStarted?.Invoke(target);
            return true;
        }

        /// <summary>把容器里第 index 格（已搜出）拿进背包。</summary>
        public bool TakeAt(int index)
        {
            if (!IsLooting || lootTarget.Contents == null || !lootTarget.Contents.IsRevealed(index))
            {
                return false;
            }

            var target = lootTarget;
            if (target.Contents.TakeAt(index, inventory.Inventory))
            {
                ItemsCollected?.Invoke(target, new TransferResult(1, 0));
                return true;
            }

            InventoryFull?.Invoke(target);
            return false;
        }

        /// <summary>拿走容器里所有已搜出的物品，放不下的留下。</summary>
        public TransferResult TakeAll()
        {
            if (!IsLooting)
            {
                return new TransferResult(0, 0);
            }

            var target = lootTarget;
            var result = target.TransferTo(inventory.Inventory);
            if (result.Moved > 0) ItemsCollected?.Invoke(target, result);
            if (result.BlockedByFullInventory) InventoryFull?.Invoke(target);
            return result;
        }

        /// <summary>关闭容器面板（同 <see cref="CloseLoot"/>），保留旧名供教学过场等调用。</summary>
        public void CancelSearch(SearchCancelReason reason) => CloseLoot(reason);

        public void CloseLoot(SearchCancelReason reason = SearchCancelReason.Interrupted)
        {
            if (!IsLooting)
            {
                return;
            }

            var target = lootTarget;
            var wasSearching = search.IsRunning;
            lootTarget = null;
            search.Cancel();
            closedFrame = Time.frameCount;
            if (disabledGameplay)
            {
                disabledGameplay = false;
                // 结算冻结玩家时组件先被停用：此时不要把操作还回去。
                if (isActiveAndEnabled) input.SetGameplayEnabled(true);
            }

            if (wasSearching) SearchCancelled?.Invoke(target, reason);
            LootClosed?.Invoke(target);
        }

        private void StartNextItem()
        {
            var seconds = lootTarget.NextItemSearchSeconds();
            if (lootTarget.Contents.NextHidden < 0)
            {
                search.Cancel();
                return;
            }

            search.Start(seconds);
        }

        private void OnDamaged(float amount)
        {
            CloseLoot(SearchCancelReason.Damaged);
        }
    }
}
