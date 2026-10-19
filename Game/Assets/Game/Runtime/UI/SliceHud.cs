using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace WildernessExtraction
{
    /// <summary>
    /// 灰盒最小 HUD：生命、体力、弹药、背包槽位、交互提示、撤离进度、提示消息、仓库、暂停和结算。
    /// 运行时用 uGUI 构建，不依赖额外 UI 资产。
    /// </summary>
    public sealed class SliceHud : MonoBehaviour
    {
        private static readonly string[] PreferredFonts = { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "PingFang SC", "Arial" };
        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color SlotEmptyColor = new Color(1f, 1f, 1f, 0.08f);

        [SerializeField] private RunSession session;
        [SerializeField, Min(0.1f)] private float toastDuration = 2f;

        private Font font;
        private RectTransform healthFill;
        private RectTransform staminaFill;
        private RectTransform actionFill;
        private RectTransform extractionFill;
        private Text healthLabel;
        private Text staminaLabel;
        private Text ammoText;
        private Text promptText;
        private Text actionLabel;
        private Text extractionLabel;
        private Text toastText;
        private Text stashText;
        private Text resultTitle;
        private Text resultBody;
        private GameObject actionBar;
        private GameObject extractionBar;
        private GameObject pausePanel;
        private GameObject resultPanel;
        private GameObject backpackPanel;
        private Text backpackSummary;
        private Image[] slotIcons;
        private Text[] slotLabels;
        private Image[] slotFrames;
        private Image[] backpackIcons;
        private Text[] backpackLabels;
        private Image[] backpackFrames;
        private float toastHideTime;
        private bool backpackOpen;
        private int selectedBackpackStack = -1;

        private PlayerCharacter Player => session.Player;

        private void Awake()
        {
            if (session == null)
            {
                session = FindAnyObjectByType<RunSession>();
            }

            font = LoadFont();
            BuildLayout();
            EnsureEventSystem();
        }

        private void Start()
        {
            Player.Interactor.InventoryFull += OnInventoryFull;
            Player.Interactor.SearchCancelled += OnSearchCancelled;
            Player.Interactor.ItemsCollected += OnItemsCollected;
            Player.Motor.DodgeRejected += OnDodgeRejected;
            session.ExtractionZone.ExtractionInterrupted += OnExtractionInterrupted;
            session.Ended += OnRunEnded;
            session.PauseChanged += OnPauseChanged;
        }

        private void OnDestroy()
        {
            if (session == null)
            {
                return;
            }

            session.Ended -= OnRunEnded;
            session.PauseChanged -= OnPauseChanged;
            if (session.ExtractionZone != null)
            {
                session.ExtractionZone.ExtractionInterrupted -= OnExtractionInterrupted;
            }

            if (Player != null)
            {
                Player.Interactor.InventoryFull -= OnInventoryFull;
                Player.Interactor.SearchCancelled -= OnSearchCancelled;
                Player.Interactor.ItemsCollected -= OnItemsCollected;
                Player.Motor.DodgeRejected -= OnDodgeRejected;
            }
        }

        private void Update()
        {
            if (!session.HasEnded && !session.IsPaused && Player.Input.InventoryPressed)
            {
                SetBackpackOpen(!backpackOpen);
            }

            var quickSlot = Player.Input.QuickSlotPressed;
            if (!session.HasEnded && !session.IsPaused && quickSlot >= 0)
            {
                if (backpackOpen) AssignSelectedToQuickSlot(quickSlot);
                else SelectQuickSlot(quickSlot);
            }

            RefreshVitals();
            RefreshAmmo();
            RefreshSlots();
            RefreshPrompt();
            RefreshExtraction();
            RefreshStash();
            RefreshBackpack();

            if (toastText.enabled && Time.unscaledTime >= toastHideTime)
            {
                toastText.enabled = false;
            }
        }

        // ---------- 刷新 ----------

        private void RefreshVitals()
        {
            var health = Player.Health;
            SetFill(healthFill, health.Normalized);
            healthLabel.text = $"生命 {Mathf.CeilToInt(health.Current)}/{Mathf.CeilToInt(health.Max)}";

            var stamina = Player.Motor.Stamina;
            if (stamina != null)
            {
                SetFill(staminaFill, stamina.Normalized);
                staminaLabel.text = $"体力 {Mathf.FloorToInt(stamina.Current)}/{Mathf.FloorToInt(stamina.Max)}";
            }
        }

        private void RefreshAmmo()
        {
            var weapon = Player.Shooter.Weapon;
            var definition = Player.Shooter.CurrentConfig;
            if (weapon == null)
            {
                return;
            }

            if (definition.kind == WeaponKind.Melee)
            {
                ammoText.text = $"{definition.displayName}  体力 {definition.staminaCost:0}";
            }
            else if (weapon.IsReloading)
            {
                ammoText.text = $"换弹中… {Mathf.RoundToInt(weapon.ReloadProgress(Time.time) * 100f)}%";
            }
            else
            {
                ammoText.text = weapon.AmmoInMagazine > 0
                    ? $"{definition.displayName}  {weapon.AmmoInMagazine}/{weapon.MagazineSize}  备弹 {Player.Inventory.Inventory.CountOf(definition.ammoItem)}"
                    : $"{definition.displayName}  0/{weapon.MagazineSize}  [R] 换弹";
            }
        }

        private void RefreshSlots()
        {
            var inventory = Player.Inventory.Inventory;
            var quickSlots = Player.Inventory.QuickSlots;
            for (var i = 0; i < slotIcons.Length; i++)
            {
                var item = quickSlots[i];
                var count = item != null ? inventory.CountOf(item) : 0;
                slotIcons[i].color = item != null
                    ? new Color(item.iconColor.r, item.iconColor.g, item.iconColor.b, count > 0 ? 1f : 0.25f)
                    : SlotEmptyColor;
                slotLabels[i].text = item != null ? $"[{i + 1}] {item.displayName}\nx{count}" : $"[{i + 1}]";
                slotFrames[i].color = quickSlots.SelectedIndex == i
                    ? new Color(1f, 0.78f, 0.2f, 0.95f)
                    : PanelColor;
            }
        }

        private void RefreshBackpack()
        {
            if (!backpackOpen) return;

            var inventory = Player.Inventory.Inventory;
            for (var i = 0; i < backpackIcons.Length; i++)
            {
                var stack = i < inventory.Stacks.Count ? inventory.Stacks[i] : null;
                backpackIcons[i].color = stack != null ? stack.Item.iconColor : SlotEmptyColor;
                backpackLabels[i].text = stack == null
                    ? $"{i + 1}"
                    : $"{stack.Item.displayName}\n<size=16>x{stack.Quantity}  {stack.OccupiedSlots}格</size>";
                backpackFrames[i].color = i == selectedBackpackStack
                    ? new Color(0.25f, 0.7f, 1f, 0.95f)
                    : PanelColor;
            }

            backpackSummary.text = $"已用 {inventory.UsedSlots}/{inventory.Capacity} 格    重量 {inventory.TotalWeight:0.0} kg";
        }

        private void RefreshPrompt()
        {
            var interactor = Player.Interactor;
            if (interactor.IsSearching)
            {
                promptText.text = string.Empty;
                actionBar.SetActive(true);
                SetFill(actionFill, interactor.SearchProgress);
                actionLabel.text = $"正在搜索 {interactor.SearchTarget.DisplayName}…";
                return;
            }

            actionBar.SetActive(false);
            var focus = interactor.isActiveAndEnabled ? interactor.Focus : null;
            if (focus == null)
            {
                promptText.text = string.Empty;
            }
            else if (focus.IsSearched)
            {
                promptText.text = $"[E] 拾取 {focus.DisplayName} 中剩余的 {focus.Remaining.Count} 件物品";
            }
            else
            {
                promptText.text = $"[E] 搜索 {focus.DisplayName}";
            }
        }

        private void RefreshExtraction()
        {
            var zone = session.ExtractionZone;
            var show = !session.HasEnded && zone.IsExtracting;
            extractionBar.SetActive(show);
            if (show)
            {
                SetFill(extractionFill, zone.Progress);
                extractionLabel.text = $"撤离中… {zone.Duration - zone.Elapsed:0.0}s（离开撤离区会中断）";
            }
        }

        private void RefreshStash()
        {
            var stash = session.Stash;
            var builder = new StringBuilder("仓库（本次运行）\n");
            if (stash.TotalItems == 0)
            {
                builder.Append("空");
            }
            else
            {
                foreach (var item in stash.ItemTypes)
                {
                    builder.Append(item.displayName).Append(" ×").Append(stash.CountOf(item)).Append('\n');
                }

                builder.Append("总价值 ").Append(stash.TotalValue);
            }

            stashText.text = builder.ToString();
        }

        // ---------- 事件反馈 ----------

        private void OnInventoryFull(LootContainer container)
        {
            ShowToast($"背包已满！剩余 {container.Remaining.Count} 件物品留在{container.DisplayName}中");
        }

        private void OnSearchCancelled(LootContainer container, SearchCancelReason reason)
        {
            switch (reason)
            {
                case SearchCancelReason.OutOfRange:
                    ShowToast("搜索取消：离开了交互范围");
                    break;
                case SearchCancelReason.Damaged:
                    ShowToast("搜索取消：受到攻击");
                    break;
            }
        }

        private void OnItemsCollected(LootContainer container, TransferResult result)
        {
            if (result.Moved > 0 && !result.BlockedByFullInventory)
            {
                ShowToast($"获得 {result.Moved} 件物品");
            }
            else if (result.Moved == 0 && !result.BlockedByFullInventory)
            {
                ShowToast($"{container.DisplayName}是空的");
            }
        }

        private void OnDodgeRejected()
        {
            ShowToast("体力不足，无法翻滚");
        }

        private void OnExtractionInterrupted()
        {
            ShowToast("撤离中断：离开了撤离区");
        }

        private void OnPauseChanged(bool paused)
        {
            if (paused) SetBackpackOpen(false, false);
            pausePanel.SetActive(paused);
        }

        private void OnRunEnded(RunResult result)
        {
            SetBackpackOpen(false, false);
            toastText.enabled = false;
            pausePanel.SetActive(false);
            resultPanel.SetActive(true);

            var stash = session.Stash;
            var builder = new StringBuilder();
            if (result.Outcome == RunOutcome.Extracted)
            {
                resultTitle.text = "撤离成功";
                resultTitle.color = new Color(0.5f, 1f, 0.55f);
                builder.Append(result.Items.Count == 0
                    ? "空手而归。\n"
                    : $"带回 {result.Items.Count} 件物品（价值 {result.Value}）：{Describe(result.Items)}\n");
            }
            else
            {
                resultTitle.text = "你倒下了";
                resultTitle.color = new Color(1f, 0.45f, 0.4f);
                builder.Append(result.Items.Count == 0
                    ? "本局没有携带物品。\n"
                    : $"本局背包物资全部丢失：{Describe(result.Items)}\n");
                builder.Append("局外仓库不受影响。\n");
            }

            builder.Append($"\n仓库合计 {stash.TotalItems} 件，总价值 {stash.TotalValue}");
            resultBody.text = builder.ToString();
        }

        private void ShowToast(string message)
        {
            toastText.text = message;
            toastText.enabled = true;
            toastHideTime = Time.unscaledTime + toastDuration;
        }

        private static string Describe(IReadOnlyList<ItemDefinition> items)
        {
            var counts = new Dictionary<ItemDefinition, int>();
            var order = new List<ItemDefinition>();
            foreach (var item in items)
            {
                if (counts.TryGetValue(item, out var count))
                {
                    counts[item] = count + 1;
                }
                else
                {
                    counts[item] = 1;
                    order.Add(item);
                }
            }

            var parts = new List<string>();
            foreach (var item in order)
            {
                parts.Add($"{item.displayName}×{counts[item]}");
            }

            return string.Join("、", parts);
        }

        // ---------- 构建 ----------

        private void BuildLayout()
        {
            var canvasObject = new GameObject("HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasObject.transform;

            var topLeft = new Vector2(0f, 1f);
            healthFill = CreateBar(root, "HealthBar", topLeft, topLeft, new Vector2(24f, -24f), new Vector2(360f, 34f),
                new Color(0.85f, 0.2f, 0.2f), out healthLabel).fill;
            staminaFill = CreateBar(root, "StaminaBar", topLeft, topLeft, new Vector2(24f, -66f), new Vector2(360f, 26f),
                new Color(0.95f, 0.8f, 0.2f), out staminaLabel).fill;

            ammoText = CreateText(root, "Ammo", 30, TextAnchor.LowerRight);
            Place(ammoText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-32f, 28f), new Vector2(480f, 50f));

            stashText = CreateText(root, "Stash", 20, TextAnchor.UpperRight);
            Place(stashText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(360f, 220f));

            BuildSlots(root);
            BuildBackpack(root);

            promptText = CreateText(root, "Prompt", 26, TextAnchor.MiddleCenter);
            Place(promptText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 128f), new Vector2(900f, 40f));

            var action = CreateBar(root, "SearchBar", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 128f),
                new Vector2(420f, 32f), new Color(0.35f, 0.75f, 1f), out actionLabel);
            actionFill = action.fill;
            actionBar = action.root.gameObject;
            actionBar.SetActive(false);

            var extraction = CreateBar(root, "ExtractionBar", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f),
                new Vector2(620f, 40f), new Color(0.3f, 0.9f, 0.45f), out extractionLabel);
            extractionFill = extraction.fill;
            extractionBar = extraction.root.gameObject;
            extractionBar.SetActive(false);

            toastText = CreateText(root, "Toast", 30, TextAnchor.MiddleCenter);
            toastText.color = new Color(1f, 0.9f, 0.5f);
            Place(toastText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(1000f, 50f));
            toastText.enabled = false;

            pausePanel = CreateOverlay(root, "PausePanel");
            var pauseText = CreateText(pausePanel.transform, "Text", 44, TextAnchor.MiddleCenter);
            Stretch(pauseText.rectTransform);
            pauseText.text = "已暂停\n<size=26>[Esc] 继续</size>";
            pausePanel.SetActive(false);

            BuildResultPanel(root);
        }

        private void BuildSlots(Transform root)
        {
            const int capacity = 8;
            const float slotSize = 76f;
            const float spacing = 8f;
            var width = capacity * slotSize + (capacity - 1) * spacing;
            var strip = NewRect("InventorySlots", root);
            Place(strip, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(width, slotSize));

            slotIcons = new Image[capacity];
            slotLabels = new Text[capacity];
            slotFrames = new Image[capacity];
            for (var i = 0; i < capacity; i++)
            {
                var slot = NewRect($"Slot{i + 1}", strip);
                Place(slot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * (slotSize + spacing), 0f), new Vector2(slotSize, slotSize));
                slotFrames[i] = AddImage(slot, PanelColor);
                slotFrames[i].raycastTarget = true;
                var button = slot.gameObject.AddComponent<Button>();
                button.targetGraphic = slotFrames[i];
                var captured = i;
                button.onClick.AddListener(() => OnQuickSlotClicked(captured));

                var icon = NewRect("Icon", slot);
                Stretch(icon, 5f);
                slotIcons[i] = AddImage(icon, SlotEmptyColor);

                slotLabels[i] = CreateText(slot, "Label", 16, TextAnchor.MiddleCenter);
                Stretch(slotLabels[i].rectTransform, 2f);
            }
        }

        private void BuildBackpack(Transform root)
        {
            backpackPanel = CreateOverlay(root, "BackpackPanel");

            var box = NewRect("BackpackBox", backpackPanel.transform);
            Place(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 760f));
            AddImage(box, new Color(0.08f, 0.09f, 0.1f, 0.97f));

            var title = CreateText(box, "Title", 38, TextAnchor.MiddleLeft);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -28f), new Vector2(430f, 54f));
            title.text = "背包  [选择物品后按 1–8 绑定]  [Tab 关闭]";

            backpackSummary = CreateText(box, "Summary", 22, TextAnchor.MiddleRight);
            Place(backpackSummary.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32f, -28f), new Vector2(420f, 54f));

            const int columns = 4;
            const int capacity = 16;
            const float size = 150f;
            const float gap = 16f;
            backpackIcons = new Image[capacity];
            backpackLabels = new Text[capacity];
            backpackFrames = new Image[capacity];
            for (var i = 0; i < capacity; i++)
            {
                var row = i / columns;
                var column = i % columns;
                var slot = NewRect($"BackpackSlot{i + 1}", box);
                Place(slot, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(48f + column * (size + gap), -112f - row * (size + gap)), new Vector2(size, size));
                backpackFrames[i] = AddImage(slot, PanelColor);
                backpackFrames[i].raycastTarget = true;
                var button = slot.gameObject.AddComponent<Button>();
                button.targetGraphic = backpackFrames[i];
                var captured = i;
                button.onClick.AddListener(() => SelectBackpackStack(captured));

                var icon = NewRect("Icon", slot);
                Stretch(icon, 7f);
                backpackIcons[i] = AddImage(icon, SlotEmptyColor);

                backpackLabels[i] = CreateText(slot, "Label", 20, TextAnchor.MiddleCenter);
                Stretch(backpackLabels[i].rectTransform, 6f);
            }

            backpackPanel.SetActive(false);
        }

        private void SelectBackpackStack(int index)
        {
            if (index < 0 || index >= Player.Inventory.Inventory.Stacks.Count) return;
            selectedBackpackStack = index;
        }

        private void OnQuickSlotClicked(int index)
        {
            if (backpackOpen) AssignSelectedToQuickSlot(index);
            else SelectQuickSlot(index);
        }

        private void SelectQuickSlot(int index)
        {
            if (!Player.Inventory.QuickSlots.Select(index)) return;
            var item = Player.Inventory.QuickSlots.SelectedItem;
            if (item != null && item.category == ItemCategory.Weapon) Player.Shooter.TryEquip(item);
        }

        private void AssignSelectedToQuickSlot(int index)
        {
            var stacks = Player.Inventory.Inventory.Stacks;
            if (selectedBackpackStack < 0 || selectedBackpackStack >= stacks.Count)
            {
                ShowToast("请先在背包中选择一个物品");
                return;
            }

            var item = stacks[selectedBackpackStack].Item;
            Player.Inventory.QuickSlots.Assign(index, item);
            if (item.category == ItemCategory.Weapon) Player.Shooter.TryEquip(item);
            ShowToast($"{item.displayName} 已绑定到快捷键 {index + 1}");
        }

        private void SetBackpackOpen(bool open, bool updateGameplay = true)
        {
            backpackOpen = open;
            if (open && selectedBackpackStack >= Player.Inventory.Inventory.Stacks.Count) selectedBackpackStack = -1;
            if (backpackPanel != null)
            {
                backpackPanel.SetActive(open);
                if (open) backpackPanel.transform.SetAsLastSibling();
            }
            if (updateGameplay) Player.Input.SetGameplayEnabled(!open);
        }

        private void BuildResultPanel(Transform root)
        {
            resultPanel = CreateOverlay(root, "ResultPanel");
            var box = NewRect("Box", resultPanel.transform);
            Place(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 480f));
            AddImage(box, new Color(0.1f, 0.1f, 0.12f, 0.95f));

            resultTitle = CreateText(box, "Title", 52, TextAnchor.UpperCenter);
            Place(resultTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(760f, 70f));

            resultBody = CreateText(box, "Body", 26, TextAnchor.UpperCenter);
            Place(resultBody.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(740f, 220f));

            var buttonRect = NewRect("RestartButton", box);
            Place(buttonRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(340f, 64f));
            var buttonImage = AddImage(buttonRect, new Color(0.25f, 0.55f, 0.3f));
            buttonImage.raycastTarget = true;
            var button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(() => session.Restart());
            var buttonLabel = CreateText(buttonRect, "Label", 28, TextAnchor.MiddleCenter);
            Stretch(buttonLabel.rectTransform);
            buttonLabel.text = "再次出发 [E]";

            resultPanel.SetActive(false);
        }

        private GameObject CreateOverlay(Transform parent, string objectName)
        {
            var overlay = NewRect(objectName, parent);
            Stretch(overlay);
            var image = AddImage(overlay, new Color(0f, 0f, 0f, 0.55f));
            image.raycastTarget = true;
            return overlay.gameObject;
        }

        private (RectTransform root, RectTransform fill) CreateBar(Transform parent, string objectName, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size, Color fillColor, out Text label)
        {
            var root = NewRect(objectName, parent);
            Place(root, anchor, pivot, position, size);
            AddImage(root, PanelColor);

            var fill = NewRect("Fill", root);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            AddImage(fill, fillColor);

            label = CreateText(root, "Label", Mathf.RoundToInt(size.y * 0.6f), TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            return (root, fill);
        }

        private Text CreateText(Transform parent, string objectName, int fontSize, TextAnchor alignment)
        {
            var rect = NewRect(objectName, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.supportRichText = true;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            return text;
        }

        private static Image AddImage(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform NewRect(string objectName, Transform parent)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void SetFill(RectTransform fill, float normalized)
        {
            fill.anchorMax = new Vector2(Mathf.Clamp01(normalized), 1f);
        }

        private static Font LoadFont()
        {
            var osFont = Font.CreateDynamicFontFromOSFont(PreferredFonts, 32);
            return osFont != null ? osFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }

            // 工程仅启用新 Input System，模块在 OnEnable 时自动分配默认 UI 动作。
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
