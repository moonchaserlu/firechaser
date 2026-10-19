using UnityEngine;
using UnityEngine.UI;

namespace WildernessExtraction
{
    /// <summary>
    /// 最小 HUD：生命、体力、弹药、背包槽位、交互提示、撤离进度、提示消息、仓库和结算。
    /// 只读取会话与玩家组件的状态和事件，不包含玩法规则。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RunHud : MonoBehaviour
    {
        private static readonly Color InfoColor = Color.white;
        private static readonly Color WarningColor = new Color(1f, 0.75f, 0.2f);
        private static readonly Color DangerColor = new Color(1f, 0.35f, 0.3f);

        [SerializeField] private RunSession session;

        [Header("状态")]
        [SerializeField] private Text healthLabel;
        [SerializeField] private RectTransform healthFill;
        [SerializeField] private Text staminaLabel;
        [SerializeField] private RectTransform staminaFill;
        [SerializeField] private Text ammoLabel;

        [Header("背包")]
        [SerializeField] private Text inventoryLabel;
        [SerializeField] private Image[] inventorySlots = new Image[0];
        [SerializeField] private Color emptySlotColor = new Color(1f, 1f, 1f, 0.12f);

        [Header("交互与撤离")]
        [SerializeField] private Text promptLabel;
        [SerializeField] private GameObject extractionPanel;
        [SerializeField] private RectTransform extractionFill;
        [SerializeField] private Text extractionLabel;

        [Header("消息、仓库与结算")]
        [SerializeField] private Text messageLabel;
        [SerializeField, Min(0.1f)] private float messageDuration = 2.5f;
        [SerializeField] private Text stashLabel;
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Text resultTitle;
        [SerializeField] private Text resultBody;
        [SerializeField] private Button restartButton;

        private PlayerCharacter player;
        private float messageHideTime;

        private void Awake()
        {
            if (session == null)
            {
                session = FindAnyObjectByType<RunSession>();
            }
        }

        private void Start()
        {
            player = session.Player;
            session.Ended += OnEnded;
            session.PauseChanged += OnPauseChanged;
            session.ExtractionZone.ExtractionStarted += OnExtractionStarted;
            session.ExtractionZone.ExtractionInterrupted += OnExtractionInterrupted;
            player.Interactor.SearchCancelled += OnSearchCancelled;
            player.Interactor.ItemsCollected += OnItemsCollected;
            player.Interactor.InventoryFull += OnInventoryFull;
            player.Motor.DodgeRejected += OnDodgeRejected;
            player.Inventory.Inventory.Changed += RefreshInventory;
            if (restartButton != null)
            {
                restartButton.onClick.AddListener(session.Restart);
            }

            SetActive(pausePanel, false);
            SetActive(resultPanel, false);
            SetActive(extractionPanel, false);
            SetActive(messageLabel, false);
            RefreshInventory();
            RefreshStash();
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.Ended -= OnEnded;
                session.PauseChanged -= OnPauseChanged;
                if (session.ExtractionZone != null)
                {
                    session.ExtractionZone.ExtractionStarted -= OnExtractionStarted;
                    session.ExtractionZone.ExtractionInterrupted -= OnExtractionInterrupted;
                }
            }

            if (player != null)
            {
                player.Interactor.SearchCancelled -= OnSearchCancelled;
                player.Interactor.ItemsCollected -= OnItemsCollected;
                player.Interactor.InventoryFull -= OnInventoryFull;
                player.Motor.DodgeRejected -= OnDodgeRejected;
                player.Inventory.Inventory.Changed -= RefreshInventory;
            }

            if (restartButton != null && session != null)
            {
                restartButton.onClick.RemoveListener(session.Restart);
            }
        }

        private void Update()
        {
            if (player == null)
            {
                return;
            }

            var health = player.Health;
            SetText(healthLabel, $"生命 {Mathf.CeilToInt(health.Current)}/{Mathf.CeilToInt(health.Max)}");
            SetFill(healthFill, health.Normalized);

            var stamina = player.Motor.Stamina;
            SetText(staminaLabel, $"体力 {Mathf.FloorToInt(stamina.Current)}/{Mathf.FloorToInt(stamina.Max)}");
            SetFill(staminaFill, stamina.Normalized);

            var weapon = player.Shooter.Weapon;
            var definition = player.Shooter.CurrentConfig;
            SetText(ammoLabel, definition.kind == WeaponKind.Melee
                ? $"{definition.displayName}  体力 {definition.staminaCost:0}"
                : weapon.IsReloading
                ? $"换弹中 {weapon.ReloadProgress(Time.time):P0}"
                : $"{definition.displayName} {weapon.AmmoInMagazine}/{weapon.MagazineSize}  备弹 {player.Inventory.Inventory.CountOf(definition.ammoItem)}");

            RefreshPrompt();
            RefreshExtraction();

            if (messageLabel != null && messageLabel.gameObject.activeSelf && Time.unscaledTime >= messageHideTime)
            {
                messageLabel.gameObject.SetActive(false);
            }
        }

        public void ShowMessage(string text, Color color)
        {
            if (messageLabel == null)
            {
                return;
            }

            messageLabel.text = text;
            messageLabel.color = color;
            messageLabel.gameObject.SetActive(true);
            messageHideTime = Time.unscaledTime + messageDuration;
        }

        private void RefreshPrompt()
        {
            if (promptLabel == null)
            {
                return;
            }

            var interactor = player.Interactor;
            string prompt = null;
            if (!session.HasEnded)
            {
                if (interactor.IsSearching)
                {
                    prompt = $"正在搜索 {interactor.SearchTarget.DisplayName}… {interactor.SearchProgress:P0}";
                }
                else if (interactor.Focus != null)
                {
                    var focus = interactor.Focus;
                    prompt = focus.IsSearched
                        ? $"[E] 拾取 {focus.DisplayName} 剩余 {focus.Remaining.Count} 件"
                        : $"[E] 搜索 {focus.DisplayName}";
                }
            }

            promptLabel.text = prompt ?? string.Empty;
            promptLabel.enabled = prompt != null;
        }

        private void RefreshExtraction()
        {
            var zone = session.ExtractionZone;
            var visible = !session.HasEnded && zone.IsExtracting;
            SetActive(extractionPanel, visible);
            if (visible)
            {
                SetFill(extractionFill, zone.Progress);
                SetText(extractionLabel, $"撤离中 {zone.Elapsed:0.0}/{zone.Duration:0.0} 秒 — 保持在区域内");
            }
        }

        private void RefreshInventory()
        {
            var inventory = player.Inventory.Inventory;
            SetText(inventoryLabel, inventory.IsFull
                ? $"背包 {inventory.Count}/{inventory.Capacity}（已满）"
                : $"背包 {inventory.Count}/{inventory.Capacity}");

            for (var i = 0; i < inventorySlots.Length; i++)
            {
                var slot = inventorySlots[i];
                if (slot == null)
                {
                    continue;
                }

                slot.gameObject.SetActive(i < inventory.Capacity);
                slot.color = i < inventory.ItemCount ? inventory.Items[i].iconColor : emptySlotColor;
            }
        }

        private void RefreshStash()
        {
            SetText(stashLabel, HudText.StashSummary(session.Stash));
        }

        private void OnItemsCollected(LootContainer container, TransferResult result)
        {
            if (result.Moved > 0)
            {
                var inventory = player.Inventory.Inventory;
                var picked = new ItemDefinition[result.Moved];
                for (var i = 0; i < result.Moved; i++)
                {
                    picked[i] = inventory.Items[inventory.ItemCount - result.Moved + i];
                }

                ShowMessage($"获得：{HudText.ItemSummary(picked)}", InfoColor);
            }
            else if (!result.BlockedByFullInventory)
            {
                ShowMessage($"{container.DisplayName} 是空的", InfoColor);
            }
        }

        private void OnInventoryFull(LootContainer container)
        {
            ShowMessage($"背包已满！{container.DisplayName} 还剩 {container.Remaining.Count} 件留在原地", WarningColor);
        }

        private void OnSearchCancelled(LootContainer container, SearchCancelReason reason)
        {
            switch (reason)
            {
                case SearchCancelReason.OutOfRange:
                    ShowMessage("搜索取消：离开了交互范围", WarningColor);
                    break;
                case SearchCancelReason.Damaged:
                    ShowMessage("搜索取消：受到攻击", DangerColor);
                    break;
            }
        }

        private void OnExtractionStarted()
        {
            ShowMessage("开始撤离，保持在撤离区内", InfoColor);
        }

        private void OnExtractionInterrupted()
        {
            ShowMessage("撤离中断：离开了撤离区", WarningColor);
        }

        private void OnDodgeRejected()
        {
            ShowMessage("体力不足，无法翻滚", WarningColor);
        }

        private void OnPauseChanged(bool paused)
        {
            SetActive(pausePanel, paused);
        }

        private void OnEnded(RunResult result)
        {
            RefreshStash();
            SetActive(pausePanel, false);
            SetActive(extractionPanel, false);
            SetActive(messageLabel, false);
            SetText(resultTitle, HudText.ResultTitle(result));
            if (resultTitle != null)
            {
                resultTitle.color = result.Outcome == RunOutcome.Extracted ? new Color(0.45f, 1f, 0.55f) : DangerColor;
            }

            SetText(resultBody, HudText.ResultBody(result, session.Stash));
            SetActive(resultPanel, true);
        }

        private static void SetText(Text label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }

        /// <summary>通过 anchorMax.x 表示进度，无需填充精灵。</summary>
        private static void SetFill(RectTransform fill, float normalized)
        {
            if (fill != null)
            {
                fill.anchorMax = new Vector2(Mathf.Clamp01(normalized), fill.anchorMax.y);
            }
        }

        private static void SetActive(Component component, bool active)
        {
            if (component != null)
            {
                component.gameObject.SetActive(active);
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
    }
}
