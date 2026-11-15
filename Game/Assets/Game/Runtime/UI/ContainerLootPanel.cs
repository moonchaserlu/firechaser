using UnityEngine;
using UnityEngine.InputSystem;

namespace WildernessExtraction
{
    /// <summary>
    /// 容器面板（IMGUI，挂在玩家身上）：左边是容器网格（箱子 3×4、尸体 1×5），物品逐件搜出（正在搜的那格显示进度，没搜到的显示“?”）；
    /// 右边是背包网格（2×4）。占 2 格的东西（枪）横跨两格画，拖着时按 R 旋转成竖放。
    /// 箱子和背包之间双向拖：拖进背包才算拿走，背包里的拖回箱子就放下；同一边里拖动就是整理位置。
    /// 右键或 Shift+左键、双击直接送到另一边；没在拖东西时 R 全部拿取；F / Tab / Esc 关闭。
    /// 没打开容器时，靠近容器在屏幕下方提示“[F] 搜索”。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ContainerLootPanel : MonoBehaviour
    {
        private const float MaxCell = 72f;
        private const float SlotGap = 6f;

        private static readonly Color PanelColor = new Color(0.085f, 0.07f, 0.058f, 0.97f);
        private static readonly Color PanelInner = new Color(0.12f, 0.1f, 0.082f, 1f);
        private static readonly Color Ember = new Color(0.95f, 0.62f, 0.28f, 1f);
        private static readonly Color EmberDim = new Color(0.55f, 0.36f, 0.18f, 1f);
        private static readonly Color GhostOk = new Color(0.95f, 0.62f, 0.28f, 0.35f);
        private static readonly Color GhostBad = new Color(1f, 0.3f, 0.25f, 0.35f);
        private static readonly Color Muted = new Color(0.76f, 0.72f, 0.66f, 1f);
        private static readonly Color TextColor = new Color(0.93f, 0.9f, 0.84f, 1f);
        private static readonly Color Warning = new Color(1f, 0.47f, 0.4f, 1f);
        private static readonly Color UncommonColor = new Color(0.45f, 0.72f, 0.95f, 1f);
        private static readonly Color RareColor = new Color(1f, 0.8f, 0.3f, 1f);

        private enum DragSource
        {
            None,
            Container,
            Bag
        }

        [SerializeField] private PlayerCharacter player;
        [Tooltip("靠近容器时显示“[F] 搜索”提示。旧灰盒场景的 SliceHud / RunHud 自带提示，检测到时自动关闭。")]
        [SerializeField] private bool showPrompt = true;

        private static Texture2D white;
        private GUIStyle title;
        private GUIStyle body;
        private GUIStyle small;
        private GUIStyle glyph;
        private GUIStyle buttonStyle;

        private DragSource dragSource;
        private int dragIndex = -1;
        private ItemDefinition dragItem;
        private bool dragRotated;
        private string feedback = string.Empty;
        private float feedbackTime = -10f;
        private Vector2 lastPointer;
        private ItemDefinition hoveredItem;

        // 上一帧的布局（脚本指针和松手判定用）。
        private Rect containerGrid;
        private Rect bagGrid;
        private float cell = MaxCell;

        /// <summary>演示录像用：由脚本控制的指针位置（GUI 坐标）。为 null 时用真鼠标。设置后会在该位置画一个指针。</summary>
        public Vector2? ScriptedPointer { get; set; }

        private Vector2 Pointer => ScriptedPointer ?? Event.current.mousePosition;
        private PlayerInteractor Interactor => player != null ? player.Interactor : null;
        private int Size => dragItem != null ? Mathf.Max(1, dragItem.size) : 1;

        /// <summary>正在拖东西。</summary>
        public bool IsDragging => dragItem != null;
        /// <summary>拖着的东西当前是否竖放（R 切换）。</summary>
        public bool DragRotated => dragRotated;

        /// <summary>容器第 index 格中心（GUI 坐标，上一帧布局）。</summary>
        public Vector2 ContainerSlotCenter(int index)
        {
            var contents = Contents;
            if (contents == null || index < 0 || index >= contents.Count) return containerGrid.center;
            return ItemRect(containerGrid, contents.PlacementAt(index)).center;
        }

        /// <summary>背包区域中心（GUI 坐标，上一帧布局）。</summary>
        public Vector2 BackpackCenter => bagGrid.center;

        /// <summary>容器区域中心（GUI 坐标，上一帧布局）。</summary>
        public Vector2 ContainerCenter => containerGrid.center;

        /// <summary>背包网格 (x, y) 那一格的中心（GUI 坐标，上一帧布局）。</summary>
        public Vector2 BagCellCenter(int x, int y) => CellRect(bagGrid, x, y).center;

        /// <summary>容器网格 (x, y) 那一格的中心（GUI 坐标，上一帧布局）。</summary>
        public Vector2 ContainerCellCenter(int x, int y) => CellRect(containerGrid, x, y).center;

        /// <summary>开始拖容器第 index 格（与鼠标按下相同）。</summary>
        public bool BeginDrag(int index)
        {
            var contents = Contents;
            if (contents == null || !contents.IsRevealed(index)) return false;
            StartDrag(DragSource.Container, index, contents[index], contents.PlacementAt(index).Rotated);
            return true;
        }

        /// <summary>开始拖背包里的第 stackIndex 堆。</summary>
        public bool BeginDragFromBag(int stackIndex)
        {
            var interactor = Interactor;
            if (interactor == null || !interactor.IsLooting || stackIndex < 0 || stackIndex >= interactor.Bag.Stacks.Count) return false;
            var stack = interactor.Bag.Stacks[stackIndex];
            var layout = interactor.Bag.Layout;
            StartDrag(DragSource.Bag, stackIndex, stack.Item, layout.TryGet(stack, out var placement) && placement.Rotated);
            return true;
        }

        /// <summary>拖着时旋转（与按 R 相同）。只对占 2 格的东西有用。</summary>
        public void RotateDrag()
        {
            if (dragItem != null && Size >= 2) dragRotated = !dragRotated;
        }

        /// <summary>在当前指针处松开（与鼠标抬起相同）。</summary>
        public bool Drop()
        {
            var interactor = Interactor;
            var contents = Contents;
            var done = false;
            if (dragItem != null && interactor != null && contents != null)
            {
                var pointer = ScriptedPointer ?? lastPointer;
                if (bagGrid.Contains(pointer)) done = DropOnBag(interactor, contents, pointer);
                else if (containerGrid.Contains(pointer)) done = DropOnContainer(interactor, contents, pointer);
                else done = DropOnQuickSlots(interactor, contents, pointer);
            }

            ClearDrag();
            return done;
        }

        private ContainerContents Contents
        {
            get
            {
                var interactor = Interactor;
                return interactor != null && interactor.IsLooting ? interactor.LootTarget.Contents : null;
            }
        }

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
                Interactor.ContainerFull += OnContainerFull;
                Interactor.LootClosed += OnLootClosed;
            }
        }

        private void OnDestroy()
        {
            if (Interactor != null)
            {
                Interactor.InventoryFull -= OnInventoryFull;
                Interactor.ContainerFull -= OnContainerFull;
                Interactor.LootClosed -= OnLootClosed;
            }
        }

        private void Update()
        {
            var interactor = Interactor;
            if (interactor == null || !interactor.IsLooting) return;
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.rKey.wasPressedThisFrame) return;
            // 拖着东西时 R 旋转；没拖时 R 全部拿取。
            if (dragItem != null) RotateDrag();
            else interactor.TakeAll();
        }

        private void OnInventoryFull(LootContainer container) => Say(Loc.T("背包满了：把用不上的东西拖回箱子腾地方"));

        private void OnContainerFull(LootContainer container) => Say(Loc.T("箱子放不下了"));

        private void Say(string text)
        {
            feedback = text;
            feedbackTime = Time.unscaledTime;
        }

        private void OnLootClosed(LootContainer container) => ClearDrag();

        private void StartDrag(DragSource source, int index, ItemDefinition item, bool rotated)
        {
            dragSource = source;
            dragIndex = index;
            dragItem = item;
            dragRotated = rotated && item != null && item.size >= 2;
        }

        private void ClearDrag()
        {
            dragSource = DragSource.None;
            dragIndex = -1;
            dragItem = null;
            dragRotated = false;
        }

        // ---------- 绘制 ----------

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
            QuickSlotHud.MarkEditing();
            if (ScriptedPointer == null) lastPointer = Event.current.mousePosition;

            var width = Mathf.Min(900f, Screen.width - 32f);
            var height = Mathf.Min(500f, Screen.height - 32f);
            var frame = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            Fill(new Rect(frame.x - 2, frame.y - 2, frame.width + 4, frame.height + 4), EmberDim);
            Fill(frame, PanelColor);

            var half = (frame.width - 60f) * 0.5f;
            var left = new Rect(frame.x + 20, frame.y + 16, half, frame.height - 86);
            var right = new Rect(left.xMax + 20, frame.y + 16, half, frame.height - 86);
            Fill(new Rect(left.xMax + 10, frame.y + 20, 1, frame.height - 96), EmberDim);

            // 两边用同一个格子大小：按更高的那个网格塞进可用高度。
            var rows = Mathf.Max(contents.Height, interactor.Bag.Layout.Height);
            cell = Mathf.Min(MaxCell, (left.height - 46f - (rows - 1) * SlotGap) / rows);

            hoveredItem = null;
            DrawContainer(left, interactor, container, contents);
            DrawBackpack(right, interactor);
            DrawFooter(new Rect(frame.x + 20, frame.yMax - 62, frame.width - 40, 46), interactor, contents);
            HandleRelease();
            DrawDragged();
        }

        private void DrawContainer(Rect area, PlayerInteractor interactor, LootContainer container, ContainerContents contents)
        {
            Label(new Rect(area.x, area.y, area.width, 30), container.DisplayName, title, Ember);
            var status = !contents.AllRevealed ? Loc.F("搜索中  {0}/{1}", contents.RevealedCount, contents.Count)
                : contents.IsEmpty ? Loc.T("已搜空") : Loc.T("已搜完");
            Label(new Rect(area.x, area.y, area.width, 30), status, body, Muted, TextAnchor.MiddleRight);
            Fill(new Rect(area.x, area.y + 34, area.width, 1), EmberDim);

            containerGrid = GridRect(area, contents.Width, contents.Height);
            DrawCells(containerGrid, contents.Width, contents.Height);
            var searching = interactor.SearchingIndex;
            for (var i = 0; i < contents.Count; i++)
            {
                var rect = ItemRect(containerGrid, contents.PlacementAt(i));
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
                if (dragSource == DragSource.Container && i == dragIndex && dragItem == item) continue;
                var placement = contents.PlacementAt(i);
                DrawItem(rect, item, placement.Rotated, contents.QuantityAt(i));
                if (rect.Contains(Pointer)) hoveredItem = item;
                var index = i;
                HandleItemPress(rect, () =>
                {
                    interactor.TakeAt(index);
                    ClearDrag();
                }, () => StartDrag(DragSource.Container, index, item, placement.Rotated));
            }

            if (dragSource == DragSource.Bag) DrawGhost(containerGrid, (x, y) => contents.Fits(dragItem, x, y, dragRotated));
            else if (dragSource == DragSource.Container) DrawGhost(containerGrid, (x, y) => FitsInContainerMoving(contents, x, y));
        }

        private void DrawBackpack(Rect area, PlayerInteractor interactor)
        {
            var bag = interactor.Bag;
            var layout = bag.Layout;
            Label(new Rect(area.x, area.y, area.width, 30), Loc.T("背包"), title, Ember);
            Label(new Rect(area.x, area.y, area.width, 30), Loc.F("{0}/{1} 格", bag.UsedSlots, bag.Capacity), body, bag.IsFull ? Warning : Muted, TextAnchor.MiddleRight);
            Fill(new Rect(area.x, area.y + 34, area.width, 1), EmberDim);

            bagGrid = GridRect(area, layout.Width, layout.Height);
            DrawCells(bagGrid, layout.Width, layout.Height);
            var stacks = bag.Stacks;
            for (var s = 0; s < stacks.Count; s++)
            {
                var stack = stacks[s];
                if (!layout.TryGet(stack, out var placement)) continue;
                if (dragSource == DragSource.Bag && s == dragIndex && dragItem == stack.Item) continue;
                var rect = ItemRect(bagGrid, placement);
                DrawItem(rect, stack.Item, placement.Rotated, stack.Quantity);
                if (rect.Contains(Pointer)) hoveredItem = stack.Item;
                var index = s;
                HandleItemPress(rect, () =>
                {
                    interactor.StoreAt(index);
                    ClearDrag();
                }, () => StartDrag(DragSource.Bag, index, stack.Item, placement.Rotated));
            }

            if (dragSource == DragSource.Bag)
            {
                var moving = DraggedStack(bag);
                DrawGhost(bagGrid, (x, y) => layout.Fits(x, y, Size, dragRotated, moving));
            }
            else if (dragSource == DragSource.Container)
            {
                DrawGhost(bagGrid, (x, y) => layout.Fits(x, y, Size, dragRotated) || CanMerge(bag, dragItem));
            }
        }

        /// <summary>鼠标按下：右键、Shift+左键、双击直接送到另一边；左键开始拖。</summary>
        private static void HandleItemPress(Rect rect, System.Action quick, System.Action beginDrag)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || !rect.Contains(e.mousePosition)) return;
            if (e.button == 1 || (e.button == 0 && (e.shift || e.clickCount >= 2)))
            {
                quick();
                e.Use();
                return;
            }

            if (e.button == 0)
            {
                beginDrag();
                e.Use();
            }
        }

        private void HandleRelease()
        {
            var e = Event.current;
            if (dragItem == null || ScriptedPointer != null) return;
            if (e.rawType != EventType.MouseUp || e.button != 0) return;
            Drop();
            e.Use();
        }

        // ---------- 松手 ----------

        private bool DropOnBag(PlayerInteractor interactor, ContainerContents contents, Vector2 pointer)
        {
            var (x, y) = TargetCell(bagGrid, pointer);
            var bag = interactor.Bag;
            if (dragSource == DragSource.Bag)
            {
                var stack = DraggedStack(bag);
                return stack != null && bag.MoveStack(stack, x, y, dragRotated);
            }

            var index = ContainerIndex(contents);
            return index >= 0 && interactor.TakeAt(index, x, y, dragRotated);
        }

        private bool DropOnContainer(PlayerInteractor interactor, ContainerContents contents, Vector2 pointer)
        {
            var (x, y) = TargetCell(containerGrid, pointer);
            if (dragSource == DragSource.Container)
            {
                var index = ContainerIndex(contents);
                return index >= 0 && contents.MoveSlot(index, x, y, dragRotated);
            }

            var stack = DraggedStack(interactor.Bag);
            if (stack == null) return false;
            return interactor.StoreAt(IndexOf(interactor.Bag, stack), x, y, dragRotated);
        }

        /// <summary>拖到底部快捷栏：背包里的东西直接装上；箱子里的先拿进背包再装上。</summary>
        private bool DropOnQuickSlots(PlayerInteractor interactor, ContainerContents contents, Vector2 pointer)
        {
            if (player == null) return false;
            var slot = QuickSlotHud.SlotAt(pointer, player.Inventory.QuickSlots.Capacity);
            if (slot < 0) return false;
            var item = dragItem;
            if (dragSource == DragSource.Container)
            {
                var index = ContainerIndex(contents);
                if (index < 0 || !interactor.TakeAt(index)) return false;
            }

            return QuickSlotHud.Drop(player, slot, item);
        }

        /// <summary>指针所在格就是拖着那件东西的左上格（拖的时候图标的左上角那格跟着指针）。</summary>
        private (int x, int y) TargetCell(Rect grid, Vector2 pointer)
        {
            var x = Mathf.FloorToInt((pointer.x - grid.x + SlotGap * 0.5f) / (cell + SlotGap));
            var y = Mathf.FloorToInt((pointer.y - grid.y + SlotGap * 0.5f) / (cell + SlotGap));
            return (x, y);
        }

        private bool FitsInContainerMoving(ContainerContents contents, int x, int y)
        {
            var index = ContainerIndex(contents);
            if (index < 0) return false;
            // 挪动时忽略它自己原来占的格子。
            var occupant = contents.IndexAt(x, y);
            return contents.Fits(dragItem, x, y, dragRotated) || occupant == index;
        }

        private int ContainerIndex(ContainerContents contents)
        {
            if (dragIndex >= 0 && dragIndex < contents.Count && contents[dragIndex] == dragItem && contents.IsRevealed(dragIndex)) return dragIndex;
            for (var i = 0; i < contents.Count; i++)
            {
                if (contents[i] == dragItem && contents.IsRevealed(i)) return i;
            }

            return -1;
        }

        private InventoryStack DraggedStack(Inventory bag)
        {
            var stacks = bag.Stacks;
            if (dragIndex >= 0 && dragIndex < stacks.Count && stacks[dragIndex].Item == dragItem) return stacks[dragIndex];
            foreach (var stack in stacks)
            {
                if (stack.Item == dragItem) return stack;
            }

            return null;
        }

        private static int IndexOf(Inventory bag, InventoryStack stack)
        {
            var stacks = bag.Stacks;
            for (var i = 0; i < stacks.Count; i++)
            {
                if (stacks[i] == stack) return i;
            }

            return -1;
        }

        private static bool CanMerge(Inventory bag, ItemDefinition item)
        {
            if (item == null || !item.IsStackable) return false;
            foreach (var stack in bag.Stacks)
            {
                if (stack.Item == item && stack.Quantity < item.maxStack) return true;
            }

            return false;
        }

        // ---------- 拖拽显示 ----------

        private void DrawGhost(Rect grid, System.Func<int, int, bool> fits)
        {
            var pointer = Pointer;
            if (!grid.Contains(pointer)) return;
            var (x, y) = TargetCell(grid, pointer);
            var placement = new GridPlacement { X = x, Y = y, Size = Size, Rotated = dragRotated };
            Fill(ItemRect(grid, placement), fits(x, y) ? GhostOk : GhostBad);
        }

        private void DrawDragged()
        {
            var mouse = Pointer;
            if (dragItem != null)
            {
                var placement = new GridPlacement { Size = Size, Rotated = dragRotated };
                var w = placement.Width * cell + (placement.Width - 1) * SlotGap;
                var h = placement.Height * cell + (placement.Height - 1) * SlotGap;
                var previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.85f);
                DrawItem(new Rect(mouse.x - cell * 0.5f, mouse.y - cell * 0.5f, w, h), dragItem, dragRotated, 0);
                GUI.color = previous;
            }

            if (ScriptedPointer == null) return;
            // 录像里看不到系统光标：画一个简单的箭头。
            Fill(new Rect(mouse.x, mouse.y, 3, 22), Color.black);
            Fill(new Rect(mouse.x, mouse.y, 16, 3), Color.black);
            Fill(new Rect(mouse.x + 1, mouse.y + 1, 2, 19), Color.white);
            Fill(new Rect(mouse.x + 1, mouse.y + 1, 13, 2), Color.white);
            for (var i = 0; i < 14; i++) Fill(new Rect(mouse.x + 2 + i, mouse.y + 2 + i, 3, 3), Color.white);
        }

        // ---------- 底栏与提示 ----------

        private void DrawFooter(Rect area, PlayerInteractor interactor, ContainerContents contents)
        {
            Fill(new Rect(area.x, area.y - 6, area.width, 1), EmberDim);
            var info = new Rect(area.x, area.y, area.width - 200, area.height);
            if (Time.unscaledTime - feedbackTime < 2f)
            {
                Label(info, feedback, body, Warning);
            }
            else if (dragItem != null && Size >= 2)
            {
                Label(info, Loc.T("[R] 旋转"), body, TextColor);
            }
            else if (hoveredItem != null)
            {
                Label(info, Loc.F("{0}    {1} · 占 {2} 格 · 价值 {3}", hoveredItem.DisplayName, CategoryName(hoveredItem.category), hoveredItem.size, hoveredItem.stashValue), body, TextColor);
            }
            else
            {
                Label(info, Loc.T("箱子和背包之间可以来回拖    拖着时 [R] 旋转    右键快速拿取 / 放回    [F] 关闭"), small, Muted);
            }

            var any = contents.RevealedCount > 0;
            var button = new Rect(area.xMax - 180, area.y + 2, 180, area.height - 4);
            Fill(button, any ? Ember : new Color(0.25f, 0.22f, 0.19f, 1f));
            buttonStyle.normal.textColor = any ? new Color(0.12f, 0.07f, 0.03f) : Muted;
            var previous = GUI.enabled;
            GUI.enabled = any;
            if (GUI.Button(button, Loc.T("全部拿取  [R]"), buttonStyle)) interactor.TakeAll();
            GUI.enabled = previous;
        }

        private void DrawPrompt(PlayerInteractor interactor)
        {
            if (!showPrompt || !player.Input.GameplayEnabled || !player.ControlEnabled) return;
            var focus = interactor.Focus;
            if (focus == null) return;
            var text = focus.IsEmpty ? Loc.F("[F] 打开 {0}（空）", focus.DisplayName)
                : focus.IsSearched ? Loc.F("[F] 打开 {0}", focus.DisplayName) : Loc.F("[F] 搜索 {0}", focus.DisplayName);
            var rect = new Rect(Screen.width * 0.5f - 140, Screen.height - 196, 280, 34);
            Fill(rect, new Color(0f, 0f, 0f, 0.55f));
            Label(rect, text, body, TextColor, TextAnchor.MiddleCenter);
        }

        // ---------- 网格工具 ----------

        /// <summary>网格在区域里水平居中、标题下方开始。</summary>
        private Rect GridRect(Rect area, int columns, int rows)
        {
            var w = columns * cell + (columns - 1) * SlotGap;
            var h = rows * cell + (rows - 1) * SlotGap;
            return new Rect(area.x + (area.width - w) * 0.5f, area.y + 46, w, h);
        }

        private Rect CellRect(Rect grid, int x, int y) => new Rect(grid.x + x * (cell + SlotGap), grid.y + y * (cell + SlotGap), cell, cell);

        private Rect ItemRect(Rect grid, GridPlacement placement)
        {
            var origin = CellRect(grid, placement.X, placement.Y);
            return new Rect(origin.x, origin.y, placement.Width * cell + (placement.Width - 1) * SlotGap, placement.Height * cell + (placement.Height - 1) * SlotGap);
        }

        private void DrawCells(Rect grid, int columns, int rows)
        {
            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < columns; x++) Fill(CellRect(grid, x, y), PanelInner);
            }
        }

        /// <summary>画一件东西：图标横跨它占的格子；竖放时图标转 90°。quantity &gt; 1 时右下角写数量。</summary>
        private void DrawItem(Rect rect, ItemDefinition item, bool rotated, int quantity)
        {
            Fill(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), RarityColor(item));
            Fill(rect, new Color(0f, 0f, 0f, 0.55f));
            if (item.icon != null)
            {
                var inner = new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6);
                if (rotated)
                {
                    // 竖放：在横放的框里画好再绕中心转 90°。
                    var matrix = GUI.matrix;
                    var center = inner.center;
                    GUIUtility.RotateAroundPivot(90f, center);
                    GUI.DrawTexture(new Rect(center.x - inner.height * 0.5f, center.y - inner.width * 0.5f, inner.height, inner.width), item.icon, ScaleMode.ScaleToFit, true);
                    GUI.matrix = matrix;
                }
                else
                {
                    GUI.DrawTexture(inner, item.icon, ScaleMode.ScaleToFit, true);
                }
            }
            else
            {
                var color = item.iconColor;
                Fill(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), color);
                if (!string.IsNullOrEmpty(item.DisplayName))
                {
                    // 还没有图标的物品：色块上写物品名的首字。
                    var previousSize = glyph.fontSize;
                    glyph.fontSize = Mathf.RoundToInt(Mathf.Min(rect.width, rect.height) * 0.5f);
                    var ink = color.grayscale > 0.55f ? new Color(0.1f, 0.08f, 0.06f, 0.9f) : new Color(1f, 0.97f, 0.9f, 0.95f);
                    Label(rect, item.DisplayName.Substring(0, 1), glyph, ink, TextAnchor.MiddleCenter);
                    glyph.fontSize = previousSize;
                }
            }

            if (quantity > 1) Label(new Rect(rect.x, rect.yMax - 22, rect.width - 6, 20), $"{quantity}", body, TextColor, TextAnchor.MiddleRight);
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
                case ItemCategory.Consumable: return Loc.T("消耗品");
                case ItemCategory.Ammo: return Loc.T("弹药");
                case ItemCategory.Material: return Loc.T("材料");
                case ItemCategory.Valuable: return Loc.T("贵重品");
                default: return Loc.T("武器");
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
