using UnityEngine;
using UnityEngine.InputSystem;

namespace WildernessExtraction
{
    /// <summary>
    /// 容器面板（IMGUI，挂在玩家身上）：左边是容器格子，物品逐件搜出（正在搜的那格显示进度，没搜到的显示“?”）；
    /// 右边是背包。把搜出的物品拖到背包里才算拿走；右键或 Shift+左键、双击快速拿取，R 全部拿取，F / Tab / Esc 关闭。
    /// 没打开容器时，靠近容器在屏幕下方提示“[F] 搜索”。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ContainerLootPanel : MonoBehaviour
    {
        private const float SlotSize = 72f;
        private const float SlotGap = 6f;

        private static readonly Color PanelColor = new Color(0.085f, 0.07f, 0.058f, 0.97f);
        private static readonly Color PanelInner = new Color(0.12f, 0.1f, 0.082f, 1f);
        private static readonly Color Ember = new Color(0.95f, 0.62f, 0.28f, 1f);
        private static readonly Color EmberDim = new Color(0.55f, 0.36f, 0.18f, 1f);
        private static readonly Color DropHighlight = new Color(0.95f, 0.62f, 0.28f, 0.18f);
        private static readonly Color Muted = new Color(0.76f, 0.72f, 0.66f, 1f);
        private static readonly Color TextColor = new Color(0.93f, 0.9f, 0.84f, 1f);
        private static readonly Color Warning = new Color(1f, 0.47f, 0.4f, 1f);
        private static readonly Color UncommonColor = new Color(0.45f, 0.72f, 0.95f, 1f);
        private static readonly Color RareColor = new Color(1f, 0.8f, 0.3f, 1f);

        [SerializeField] private PlayerCharacter player;
        [Tooltip("靠近容器时显示“[F] 搜索”提示。旧灰盒场景的 SliceHud / RunHud 自带提示，检测到时自动关闭。")]
        [SerializeField] private bool showPrompt = true;

        private static Texture2D white;
        private GUIStyle title;
        private GUIStyle body;
        private GUIStyle small;
        private GUIStyle glyph;
        private GUIStyle buttonStyle;
        private int dragIndex = -1;
        private ItemDefinition dragItem;
        private string feedback = string.Empty;
        private float feedbackTime = -10f;
        private Rect lastGrid;
        private int lastPerRow = 1;
        private Rect lastBagArea;

        /// <summary>演示录像用：由脚本控制的指针位置（GUI 坐标）。为 null 时用真鼠标。设置后会在该位置画一个指针。</summary>
        public Vector2? ScriptedPointer { get; set; }

        private Vector2 Pointer => ScriptedPointer ?? Event.current.mousePosition;

        /// <summary>容器第 index 格中心（GUI 坐标，上一帧布局）。</summary>
        public Vector2 ContainerSlotCenter(int index) => SlotRect(lastGrid, index, lastPerRow).center;

        /// <summary>背包区域中心（GUI 坐标，上一帧布局）。</summary>
        public Vector2 BackpackCenter => lastBagArea.center;

        /// <summary>开始拖第 index 格（与鼠标按下相同）。</summary>
        public bool BeginDrag(int index)
        {
            var interactor = Interactor;
            var contents = interactor != null && interactor.IsLooting ? interactor.LootTarget.Contents : null;
            if (contents == null || !contents.IsRevealed(index)) return false;
            dragIndex = index;
            dragItem = contents[index];
            return true;
        }

        /// <summary>在当前指针处松开（与鼠标抬起相同）：落在背包里就拿走。</summary>
        public bool Drop()
        {
            var interactor = Interactor;
            if (dragItem == null || interactor == null || !interactor.IsLooting) return false;
            var contents = interactor.LootTarget.Contents;
            var taken = false;
            if (lastBagArea.Contains(ScriptedPointer ?? lastPointer))
            {
                // 拖拽期间又搜出了新物品不会改变已有格子的下标；保险起见按物品再找一次。
                var index = dragIndex < contents.Count && contents[dragIndex] == dragItem ? dragIndex : IndexOfRevealed(contents, dragItem);
                if (index >= 0) taken = interactor.TakeAt(index);
            }

            dragIndex = -1;
            dragItem = null;
            return taken;
        }

        private Vector2 lastPointer;

        private PlayerInteractor Interactor => player != null ? player.Interactor : null;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerCharacter>();
        }

        private void Start()
        {
            if (FindAnyObjectByType<SliceHud>() != null || FindAnyObjectByType<RunHud>() != null) showPrompt = false;
            if (Interactor != null)
            {
                Interactor.InventoryFull += OnInventoryFull;
                Interactor.LootClosed += OnLootClosed;
            }
        }

        private void OnDestroy()
        {
            if (Interactor != null)
            {
                Interactor.InventoryFull -= OnInventoryFull;
                Interactor.LootClosed -= OnLootClosed;
            }
        }

        private void Update()
        {
            var interactor = Interactor;
            if (interactor == null || !interactor.IsLooting) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame) interactor.TakeAll();
        }

        private void OnInventoryFull(LootContainer container)
        {
            feedback = "背包满了，放不下";
            feedbackTime = Time.unscaledTime;
        }

        private void OnLootClosed(LootContainer container)
        {
            dragIndex = -1;
            dragItem = null;
        }

        private void OnGUI()
        {
            var interactor = Interactor;
            if (interactor == null) return;
            EnsureStyles();
            if (!interactor.IsLooting)
            {
                DrawPrompt(interactor);
                return;
            }

            var container = interactor.LootTarget;
            var contents = container.Contents;
            if (contents == null) return;
            GUI.depth = -20;

            var width = Mathf.Min(900f, Screen.width - 32f);
            var height = Mathf.Min(470f, Screen.height - 32f);
            var frame = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            Fill(new Rect(frame.x - 2, frame.y - 2, frame.width + 4, frame.height + 4), EmberDim);
            Fill(frame, PanelColor);

            var half = (frame.width - 60f) * 0.5f;
            var left = new Rect(frame.x + 20, frame.y + 16, half, frame.height - 86);
            var right = new Rect(left.xMax + 20, frame.y + 16, half, frame.height - 86);
            Fill(new Rect(left.xMax + 10, frame.y + 20, 1, frame.height - 96), EmberDim);

            var hovered = DrawContainer(left, interactor, container, contents);
            var bagArea = DrawBackpack(right, interactor.Bag);
            DrawFooter(new Rect(frame.x + 20, frame.yMax - 62, frame.width - 40, 46), interactor, contents, hovered);
            HandleDrop(interactor, contents, bagArea);
            DrawDragged();
        }

        // ---------- 容器 ----------

        private ItemDefinition DrawContainer(Rect area, PlayerInteractor interactor, LootContainer container, ContainerContents contents)
        {
            Label(new Rect(area.x, area.y, area.width, 30), container.DisplayName, title, Ember);
            var status = !contents.AllRevealed ? $"搜索中  {contents.RevealedCount}/{contents.Count}"
                : contents.IsEmpty ? "已搜空" : "已搜完";
            Label(new Rect(area.x, area.y, area.width, 30), status, body, Muted, TextAnchor.MiddleRight);
            Fill(new Rect(area.x, area.y + 34, area.width, 1), EmberDim);

            var grid = new Rect(area.x, area.y + 46, area.width, area.height - 46);
            if (contents.IsEmpty)
            {
                Label(grid, "里面什么也没有了", body, Muted, TextAnchor.MiddleCenter);
                return null;
            }

            var perRow = PerRow(grid);
            lastGrid = grid;
            lastPerRow = perRow;
            var searching = interactor.SearchingIndex;
            var e = Event.current;
            ItemDefinition hovered = null;
            for (var i = 0; i < contents.Count; i++)
            {
                var rect = SlotRect(grid, i, perRow);
                if (!contents.IsRevealed(i))
                {
                    Fill(rect, PanelInner);
                    if (i == searching)
                    {
                        var progress = interactor.SearchProgress;
                        Fill(new Rect(rect.x, rect.yMax - rect.height * progress, rect.width, rect.height * progress), new Color(0.95f, 0.62f, 0.28f, 0.28f));
                        Label(rect, Spinner(), glyph, Ember, TextAnchor.MiddleCenter);
                    }
                    else
                    {
                        Label(rect, "?", glyph, new Color(0.4f, 0.36f, 0.32f, 1f), TextAnchor.MiddleCenter);
                    }

                    continue;
                }

                var item = contents[i];
                if (i == dragIndex && dragItem == item)
                {
                    Fill(rect, PanelInner);
                    continue;
                }

                Fill(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), RarityColor(item));
                Icon(rect, item);
                var quantity = contents.QuantityAt(i);
                if (quantity > 1) Label(new Rect(rect.x, rect.yMax - 22, rect.width - 6, 20), $"{quantity}", body, TextColor, TextAnchor.MiddleRight);
                if (rect.Contains(Pointer)) hovered = item;
                HandleSlotInput(interactor, rect, i, item);
            }

            return hovered;
        }

        private void HandleSlotInput(PlayerInteractor interactor, Rect rect, int index, ItemDefinition item)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || !rect.Contains(e.mousePosition)) return;
            // 右键、Shift+左键、双击：直接放进背包。
            if (e.button == 1 || (e.button == 0 && (e.shift || e.clickCount >= 2)))
            {
                interactor.TakeAt(index);
                dragIndex = -1;
                dragItem = null;
                e.Use();
                return;
            }

            if (e.button == 0)
            {
                dragIndex = index;
                dragItem = item;
                e.Use();
            }
        }

        private void HandleDrop(PlayerInteractor interactor, ContainerContents contents, Rect bagArea)
        {
            var e = Event.current;
            if (ScriptedPointer == null) lastPointer = e.mousePosition;
            if (dragItem == null || ScriptedPointer != null) return;
            if (e.rawType != EventType.MouseUp || e.button != 0) return;
            Drop();
            e.Use();
        }

        private void DrawDragged()
        {
            var mouse = Pointer;
            if (dragItem != null) Icon(new Rect(mouse.x - SlotSize * 0.4f, mouse.y - SlotSize * 0.4f, SlotSize * 0.8f, SlotSize * 0.8f), dragItem);
            if (ScriptedPointer == null) return;
            // 录像里看不到系统光标：画一个简单的箭头。
            Fill(new Rect(mouse.x, mouse.y, 3, 22), Color.black);
            Fill(new Rect(mouse.x, mouse.y, 16, 3), Color.black);
            Fill(new Rect(mouse.x + 1, mouse.y + 1, 2, 19), Color.white);
            Fill(new Rect(mouse.x + 1, mouse.y + 1, 13, 2), Color.white);
            for (var i = 0; i < 14; i++) Fill(new Rect(mouse.x + 2 + i, mouse.y + 2 + i, 3, 3), Color.white);
        }

        private static int IndexOfRevealed(ContainerContents contents, ItemDefinition item)
        {
            for (var i = 0; i < contents.Count; i++)
            {
                if (contents[i] == item && contents.IsRevealed(i)) return i;
            }

            return -1;
        }

        // ---------- 背包 ----------

        /// <summary>画背包格子，返回可以放下物品的区域。</summary>
        private Rect DrawBackpack(Rect area, Inventory bag)
        {
            Label(new Rect(area.x, area.y, area.width, 30), "背包", title, Ember);
            Label(new Rect(area.x, area.y, area.width, 30), $"{bag.UsedSlots}/{bag.Capacity} 格", body, bag.IsFull ? Warning : Muted, TextAnchor.MiddleRight);
            Fill(new Rect(area.x, area.y + 34, area.width, 1), EmberDim);

            var grid = new Rect(area.x, area.y + 46, area.width, area.height - 46);
            lastBagArea = area;
            if (dragItem != null && area.Contains(Pointer)) Fill(grid, DropHighlight);
            var perRow = PerRow(grid);
            var cell = 0;
            foreach (var stack in bag.Stacks)
            {
                for (var part = 0; part < stack.OccupiedSlots; part++, cell++)
                {
                    var rect = SlotRect(grid, cell, perRow);
                    Icon(rect, stack.Item, part == 0);
                    if (part == 0 && stack.Quantity > 1) Label(new Rect(rect.x, rect.yMax - 22, rect.width - 6, 20), $"{stack.Quantity}", body, TextColor, TextAnchor.MiddleRight);
                }
            }

            for (; cell < bag.Capacity; cell++) Fill(SlotRect(grid, cell, perRow), PanelInner);
            return area;
        }

        // ---------- 底栏与提示 ----------

        private void DrawFooter(Rect area, PlayerInteractor interactor, ContainerContents contents, ItemDefinition hovered)
        {
            Fill(new Rect(area.x, area.y - 6, area.width, 1), EmberDim);
            var info = new Rect(area.x, area.y, area.width - 200, area.height);
            if (Time.unscaledTime - feedbackTime < 2f)
            {
                Label(info, feedback, body, Warning);
            }
            else if (hovered != null)
            {
                Label(info, $"{hovered.displayName}    {CategoryName(hovered.category)} · 占 {hovered.size} 格 · 价值 {hovered.stashValue}", body, TextColor);
            }
            else
            {
                Label(info, "拖进背包拿走    右键快速拿取    [F] 关闭", small, Muted);
            }

            var any = contents.RevealedCount > 0;
            var button = new Rect(area.xMax - 180, area.y + 2, 180, area.height - 4);
            Fill(button, any ? Ember : new Color(0.25f, 0.22f, 0.19f, 1f));
            buttonStyle.normal.textColor = any ? new Color(0.12f, 0.07f, 0.03f) : Muted;
            var previous = GUI.enabled;
            GUI.enabled = any;
            if (GUI.Button(button, "全部拿取  [R]", buttonStyle)) interactor.TakeAll();
            GUI.enabled = previous;
        }

        private void DrawPrompt(PlayerInteractor interactor)
        {
            if (!showPrompt || !player.Input.GameplayEnabled || !player.ControlEnabled) return;
            var focus = interactor.Focus;
            if (focus == null) return;
            var text = focus.IsEmpty ? $"[F] 打开 {focus.DisplayName}（空）"
                : focus.IsSearched ? $"[F] 打开 {focus.DisplayName}" : $"[F] 搜索 {focus.DisplayName}";
            var rect = new Rect(Screen.width * 0.5f - 140, Screen.height - 196, 280, 34);
            Fill(rect, new Color(0f, 0f, 0f, 0.55f));
            Label(rect, text, body, TextColor, TextAnchor.MiddleCenter);
        }

        // ---------- 工具 ----------

        private static int PerRow(Rect grid) => Mathf.Max(1, Mathf.FloorToInt((grid.width + SlotGap) / (SlotSize + SlotGap)));

        private static Rect SlotRect(Rect area, int cell, int perRow)
        {
            return new Rect(area.x + cell % perRow * (SlotSize + SlotGap), area.y + cell / perRow * (SlotSize + SlotGap), SlotSize, SlotSize);
        }

        private static string Spinner()
        {
            switch (Mathf.FloorToInt(Time.unscaledTime * 4f) % 3)
            {
                case 0: return "·";
                case 1: return "··";
                default: return "···";
            }
        }

        private static Color RarityColor(ItemDefinition item)
        {
            switch (item.rarity)
            {
                case ItemRarity.Rare: return RareColor;
                case ItemRarity.Uncommon: return UncommonColor;
                default: return EmberDim;
            }
        }

        private static string CategoryName(ItemCategory category)
        {
            switch (category)
            {
                case ItemCategory.Consumable: return "消耗品";
                case ItemCategory.Ammo: return "弹药";
                case ItemCategory.Material: return "材料";
                case ItemCategory.Valuable: return "贵重品";
                default: return "武器";
            }
        }

        private void EnsureStyles()
        {
            if (white == null)
            {
                white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }

            title ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            body ??= new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            small ??= new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            glyph ??= new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0) };
            buttonStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        private void Icon(Rect rect, ItemDefinition item, bool lit = true)
        {
            Fill(rect, new Color(0f, 0f, 0f, 0.5f));
            if (item == null) return;
            if (item.icon != null)
            {
                var previousColor = GUI.color;
                GUI.color = lit ? Color.white : new Color(0.45f, 0.42f, 0.4f, 1f);
                GUI.DrawTexture(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), item.icon, ScaleMode.ScaleToFit, true);
                GUI.color = previousColor;
                return;
            }

            var color = item.iconColor;
            Fill(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), color);
            if (string.IsNullOrEmpty(item.displayName)) return;
            // 还没有图标的物品：色块上写物品名的首字。
            var previousSize = glyph.fontSize;
            glyph.fontSize = Mathf.RoundToInt(rect.height * 0.5f);
            var ink = color.grayscale > 0.55f ? new Color(0.1f, 0.08f, 0.06f, 0.9f) : new Color(1f, 0.97f, 0.9f, 0.95f);
            Label(rect, item.displayName.Substring(0, 1), glyph, ink, TextAnchor.MiddleCenter);
            glyph.fontSize = previousSize;
        }

        private static void Label(Rect rect, string text, GUIStyle style, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var previous = style.alignment;
            style.alignment = anchor;
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
            style.alignment = previous;
        }

        private static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, white);
            GUI.color = previous;
        }
    }
}
