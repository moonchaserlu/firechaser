using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 背包与合成面板（IMGUI），两个入口：在工作台按 F 打开，只有合成，世界暂停；任何地方按 Tab 打开背包，世界不停，
    /// 先看到物品页，顶部切到合成页。合成页左侧按分组列出配方，右侧显示说明、材料与制作按钮，两个入口的配方和解锁条件相同。
    /// A/D 或 ←/→ 切页，W/S 或 ↑/↓ 选配方，E / Enter 制作，Esc 离开；也可以用鼠标点。合成请求照常走权威层。
    /// </summary>
    public enum BackpackPage { Items, Crafting }

    [DisallowMultipleComponent]
    public sealed class Map01CraftingPanel : MonoBehaviour
    {
        private const float RowHeight = 46f;
        private const float GroupHeight = 24f;
        private const float SlotSize = 76f;
        private const float SlotGap = 6f;

        private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color PanelColor = new Color(0.085f, 0.07f, 0.058f, 0.97f);
        private static readonly Color PanelInner = new Color(0.12f, 0.1f, 0.082f, 1f);
        private static readonly Color Ember = new Color(0.95f, 0.62f, 0.28f, 1f);
        private static readonly Color EmberDim = new Color(0.55f, 0.36f, 0.18f, 1f);
        private static readonly Color RowSelected = new Color(0.32f, 0.2f, 0.1f, 1f);
        private static readonly Color RowHover = new Color(0.18f, 0.14f, 0.1f, 1f);
        private static readonly Color Ready = new Color(0.56f, 0.88f, 0.52f, 1f);
        private static readonly Color Missing = new Color(1f, 0.47f, 0.4f, 1f);
        private static readonly Color Muted = new Color(0.76f, 0.72f, 0.66f, 1f);
        private static readonly Color Text = new Color(0.93f, 0.9f, 0.84f, 1f);

        [SerializeField] private Map01Authority authority;
        [SerializeField] private Map01RunFlow flow;
        [SerializeField] private Map01Interactor interactor;

        private static Texture2D white;
        private GUIStyle title;
        private GUIStyle body;
        private GUIStyle small;
        private GUIStyle button;
        private GUIStyle glyph;
        private Vector2 scroll;
        private string feedback = string.Empty;
        private Color feedbackColor;
        private float feedbackTime = -10f;
        private bool openLastFrame;
        private int selectedStack;

        public int Selected { get; private set; }
        public bool IsOpen => flow != null && (flow.IsCrafting || flow.IsBackpackOpen);
        private bool IsBackpack => flow != null && flow.IsBackpackOpen;
        /// <summary>背包当前页；每次打开背包都回到物品页。</summary>
        public BackpackPage Page { get; set; } = BackpackPage.Items;
        /// <summary>工作台只有合成；背包要切到合成页才能制作。</summary>
        public bool ShowingCrafting => IsOpen && (!IsBackpack || Page == BackpackPage.Crafting);

        private IReadOnlyList<CraftingRecipe> Recipes => authority.Recipes;

        private void Start()
        {
            if (authority == null) authority = FindAnyObjectByType<Map01Authority>();
            if (flow == null) flow = FindAnyObjectByType<Map01RunFlow>();
            if (interactor == null) interactor = FindAnyObjectByType<Map01Interactor>();
            flow.BackpackOpened += OnBackpackOpened;
        }

        private void OnDestroy()
        {
            if (flow != null) flow.BackpackOpened -= OnBackpackOpened;
        }

        private void OnBackpackOpened()
        {
            Page = BackpackPage.Items;
        }

        private void Update()
        {
            // 打开面板的那一下 E 不算制作：面板至少开过一帧才接收按键。
            var open = IsOpen;
            var accept = open && openLastFrame;
            openLastFrame = open;
            if (!accept) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (IsBackpack)
            {
                if (dragStack != null && keyboard.rKey.wasPressedThisFrame && dragStack.OccupiedSlots >= 2) dragRotated = !dragRotated;
                if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame) Page = BackpackPage.Items;
                if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame) Page = BackpackPage.Crafting;
            }

            if (!ShowingCrafting || Recipes.Count == 0) return;
            if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame) Select(Selected - 1);
            if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame) Select(Selected + 1);
            if (keyboard.fKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) CraftSelected();
        }

        public void Select(int index)
        {
            if (Recipes.Count == 0) return;
            Selected = Mathf.Clamp(index, 0, Recipes.Count - 1);
        }

        /// <summary>制作当前选中的配方。</summary>
        public InteractionResult CraftSelected()
        {
            if (!ShowingCrafting || Recipes.Count == 0) return InteractionResult.InvalidState;
            var recipe = Recipes[Selected];
            var result = IsBackpack
                ? interactor.SendFieldCraft(Selected)
                : interactor.SendTo(flow.CraftingStation, InteractionAction.Craft, Selected);
            feedbackTime = Time.unscaledTime;
            if (result == InteractionResult.Accepted)
            {
                feedback = Loc.F("做好了：{0}", ResultName(recipe));
                feedbackColor = Ready;
            }
            else
            {
                feedback = result switch
                {
                    InteractionResult.Locked => Loc.T("还没学会这个做法"),
                    InteractionResult.MissingItem => Loc.T("材料不够"),
                    InteractionResult.InventoryFull => Loc.T("背包放不下"),
                    _ => Loc.T("现在做不了")
                };
                feedbackColor = Missing;
            }

            return result;
        }

        private void OnGUI()
        {
            if (!IsOpen || authority == null) return;
            GUI.depth = -10;
            EnsureStyles();

            // 背包打开时世界还在走，背景压得浅一些，看得见周围。
            Fill(new Rect(0, 0, Screen.width, Screen.height), IsBackpack ? new Color(0f, 0f, 0f, 0.25f) : Backdrop);
            var bag = authority.Player.Inventory.Inventory;
            var width = Mathf.Min(860f, Screen.width - 32f);
            var height = Mathf.Min(520f, Screen.height - 32f);
            var frame = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            Fill(new Rect(frame.x - 2, frame.y - 2, frame.width + 4, frame.height + 4), EmberDim);
            Fill(frame, PanelColor);

            Label(new Rect(frame.x + 24, frame.y + 16, 400, 30), IsBackpack ? Loc.T("背包") : Loc.T("庇护所工作台"), title, Ember);
            if (IsBackpack) DrawTabs(new Rect(frame.x + 20, frame.y + 46, frame.width - 40, 30));
            else Label(new Rect(frame.x + 24, frame.y + 48, 520, 20), Loc.T("借着炉火的光，把捡回来的东西拼成能用的。"), small, Muted);
            var info = IsBackpack ? Loc.F("背包 {0}/{1} 格      [Tab] 关闭", bag.UsedSlots, bag.Capacity) : Loc.F("背包 {0}/{1} 格      [Esc] 离开", bag.UsedSlots, bag.Capacity);
            Label(new Rect(frame.xMax - 324, frame.y + 22, 300, 22), info, body, Muted, TextAnchor.MiddleRight);
            Fill(new Rect(frame.x + 20, frame.y + 76, frame.width - 40, 1), EmberDim);

            var content = new Rect(frame.x + 20, frame.y + 88, frame.width - 40, frame.height - 108);
            if (!ShowingCrafting)
            {
                DrawItemsPage(content, bag);
                return;
            }

            if (Recipes.Count == 0)
            {
                Label(content, Loc.T("还没有能做的东西。"), body, Muted, TextAnchor.MiddleCenter);
                return;
            }

            Selected = Mathf.Clamp(Selected, 0, Recipes.Count - 1);
            var listWidth = Mathf.Min(300f, content.width * 0.4f);
            DrawList(new Rect(content.x, content.y, listWidth, content.height), bag);
            DrawDetail(new Rect(content.x + listWidth + 16, content.y, content.width - listWidth - 16, content.height), bag);
        }

        private void DrawTabs(Rect area)
        {
            DrawTab(new Rect(area.x, area.y, 110, area.height), Loc.T("物品  [A]"), BackpackPage.Items);
            DrawTab(new Rect(area.x + 118, area.y, 110, area.height), Loc.T("合成  [D]"), BackpackPage.Crafting);
            Label(new Rect(area.x + 240, area.y, area.width - 240, area.height), Loc.T("背包打开时世界不会暂停"), small, Muted, TextAnchor.MiddleRight);
        }

        private void DrawTab(Rect rect, string text, BackpackPage target)
        {
            var active = Page == target;
            if (active) Fill(rect, RowSelected);
            else if (rect.Contains(Event.current.mousePosition)) Fill(rect, RowHover);
            if (active) Fill(new Rect(rect.x, rect.yMax - 2, rect.width, 2), Ember);
            Label(rect, text, body, active ? Text : Muted, TextAnchor.MiddleCenter);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) Page = target;
        }

        /// <summary>
        /// 物品页：左边是背包网格（2 列；占 2 格的枪横跨两格，竖放时图标转 90°），右边是选中物品的信息。
        /// 拖着物品可以整理位置，拖的时候按 R 旋转；放不下的位置显示红色。
        /// </summary>
        private void DrawItemsPage(Rect area, Inventory bag)
        {
            var layout = bag.Layout;
            var cellSize = Mathf.Min(SlotSize, (area.height - 8f - (layout.Height - 1) * SlotGap) / layout.Height);
            var gridWidth = layout.Width * cellSize + (layout.Width - 1) * SlotGap;
            var grid = new Rect(area.x + 12, area.y + 4, gridWidth, layout.Height * cellSize + (layout.Height - 1) * SlotGap);
            selectedStack = bag.Stacks.Count == 0 ? -1 : Mathf.Clamp(selectedStack, 0, bag.Stacks.Count - 1);
            if (dragStack != null && !Contains(bag, dragStack)) dragStack = null;

            for (var row = 0; row < layout.Height; row++)
            {
                for (var column = 0; column < layout.Width; column++) Fill(GridCell(grid, cellSize, column, row), PanelInner);
            }

            var e = Event.current;
            for (var i = 0; i < bag.Stacks.Count; i++)
            {
                var stack = bag.Stacks[i];
                if (!layout.TryGet(stack, out var placement) || stack == dragStack) continue;
                var rect = GridItem(grid, cellSize, placement);
                if (i == selectedStack) Fill(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), Ember);
                GridIcon(rect, stack.Item, placement.Rotated);
                if (stack.Quantity > 1) Label(new Rect(rect.x, rect.yMax - 22, rect.width - 6, 20), $"{stack.Quantity}", body, Text, TextAnchor.MiddleRight);
                if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
                {
                    selectedStack = i;
                    dragStack = stack;
                    dragRotated = placement.Rotated;
                    e.Use();
                }
            }

            if (dragStack != null)
            {
                var size = dragStack.OccupiedSlots;
                var target = GridTarget(grid, cellSize, e.mousePosition);
                var ghost = new GridPlacement { X = target.x, Y = target.y, Size = size, Rotated = dragRotated };
                if (grid.Contains(e.mousePosition))
                {
                    var ok = layout.Fits(target.x, target.y, size, dragRotated, dragStack);
                    Fill(GridItem(grid, cellSize, ghost), ok ? new Color(Ember.r, Ember.g, Ember.b, 0.35f) : new Color(1f, 0.3f, 0.25f, 0.35f));
                }

                var floating = new GridPlacement { Size = size, Rotated = dragRotated };
                var preview = GridItem(new Rect(e.mousePosition.x - cellSize * 0.5f, e.mousePosition.y - cellSize * 0.5f, 0, 0), cellSize, floating);
                GridIcon(preview, dragStack.Item, dragRotated);
                if (e.rawType == EventType.MouseUp && e.button == 0)
                {
                    if (grid.Contains(e.mousePosition)) bag.MoveStack(dragStack, target.x, target.y, dragRotated);
                    dragStack = null;
                    e.Use();
                }
            }

            Label(new Rect(grid.x, grid.yMax + 6, Mathf.Max(260f, gridWidth), 20), Loc.T("拖动整理背包，拖着时 [R] 旋转"), small, Muted);

            var detail = new Rect(grid.xMax + 24, area.y + 4, area.xMax - grid.xMax - 24, area.height - 4);
            Fill(detail, PanelInner);
            if (selectedStack < 0)
            {
                Label(detail, Loc.T("背包是空的"), body, Muted, TextAnchor.MiddleCenter);
                return;
            }

            var selected = bag.Stacks[selectedStack];
            var x = detail.x + 16;
            var width = detail.width - 32;
            Icon(new Rect(x, detail.y + 16, 64, 64), selected.Item, true);
            Label(new Rect(x, detail.y + 90, width, 30), selected.Item.DisplayName, title, Text);
            Label(new Rect(x, detail.y + 124, width, 22), Loc.F("数量  {0}", selected.Quantity), body, Muted);
            Label(new Rect(x, detail.y + 148, width, 22), Loc.F("类别  {0}", CategoryName(selected.Item.category)), body, Muted);
            Label(new Rect(x, detail.y + 172, width, 22), Loc.F("占用  {0} 格", selected.OccupiedSlots), body, Muted);
            Label(new Rect(x, detail.y + 196, width, 22), Loc.F("价值  {0}", selected.Item.stashValue), body, Muted);
        }

        private static string CategoryName(ItemCategory category) => category switch
        {
            ItemCategory.Consumable => Loc.T("消耗品"),
            ItemCategory.Ammo => Loc.T("弹药"),
            ItemCategory.Material => Loc.T("材料"),
            ItemCategory.Valuable => Loc.T("贵重品"),
            _ => Loc.T("武器")
        };

        private InventoryStack dragStack;
        private bool dragRotated;

        /// <summary>背包物品页里正在拖的那堆（测试用）。</summary>
        public bool IsDraggingItem => dragStack != null;

        private static bool Contains(Inventory bag, InventoryStack stack)
        {
            foreach (var candidate in bag.Stacks)
            {
                if (candidate == stack) return true;
            }

            return false;
        }

        private static Rect GridCell(Rect grid, float size, int x, int y) => new Rect(grid.x + x * (size + SlotGap), grid.y + y * (size + SlotGap), size, size);

        private static Rect GridItem(Rect grid, float size, GridPlacement placement)
        {
            var origin = GridCell(grid, size, placement.X, placement.Y);
            return new Rect(origin.x, origin.y, placement.Width * size + (placement.Width - 1) * SlotGap, placement.Height * size + (placement.Height - 1) * SlotGap);
        }

        private static (int x, int y) GridTarget(Rect grid, float size, Vector2 pointer) =>
            (Mathf.FloorToInt((pointer.x - grid.x + SlotGap * 0.5f) / (size + SlotGap)), Mathf.FloorToInt((pointer.y - grid.y + SlotGap * 0.5f) / (size + SlotGap)));

        /// <summary>图标横跨它占的格子；竖放时绕中心转 90°。没有图标时退回原来的色块画法。</summary>
        private void GridIcon(Rect rect, ItemDefinition item, bool rotated)
        {
            if (item == null) return;
            if (item.icon == null)
            {
                Icon(rect, item, true);
                return;
            }

            Fill(rect, new Color(0f, 0f, 0f, 0.5f));
            var inner = new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6);
            if (!rotated)
            {
                GUI.DrawTexture(inner, item.icon, ScaleMode.ScaleToFit, true);
                return;
            }

            var matrix = GUI.matrix;
            var center = inner.center;
            GUIUtility.RotateAroundPivot(90f, center);
            GUI.DrawTexture(new Rect(center.x - inner.height * 0.5f, center.y - inner.width * 0.5f, inner.height, inner.width), item.icon, ScaleMode.ScaleToFit, true);
            GUI.matrix = matrix;
        }

        private static Rect SlotRect(Rect area, int cell, int perRow)
        {
            return new Rect(area.x + cell % perRow * (SlotSize + SlotGap), area.y + cell / perRow * (SlotSize + SlotGap), SlotSize, SlotSize);
        }

        private void DrawList(Rect area, Inventory bag)
        {
            Fill(area, PanelInner);
            var rows = 0f;
            string group = null;
            foreach (var recipe in Recipes)
            {
                if (recipe.category != group) { group = recipe.category; rows += GroupHeight; }
                rows += RowHeight;
            }

            var view = new Rect(0, 0, area.width - 16, rows + 8);
            scroll = GUI.BeginScrollView(area, scroll, view, false, false);
            var y = 4f;
            group = null;
            for (var i = 0; i < Recipes.Count; i++)
            {
                var recipe = Recipes[i];
                if (recipe.category != group)
                {
                    group = recipe.category;
                    Label(new Rect(12, y + 4, view.width - 24, 18), string.IsNullOrEmpty(group) ? Loc.T("其他") : Loc.T(group), small, Ember);
                    y += GroupHeight;
                }

                var row = new Rect(4, y, view.width - 4, RowHeight - 4);
                var unlocked = authority.IsRecipeUnlocked(recipe);
                var ready = unlocked && Crafting.HasIngredients(bag, recipe);
                if (i == Selected) Fill(row, RowSelected);
                else if (row.Contains(Event.current.mousePosition)) Fill(row, RowHover);
                if (i == Selected) Fill(new Rect(row.x, row.y, 3, row.height), Ember);

                Icon(new Rect(row.x + 12, row.y + 7, 28, 28), recipe.result, unlocked);
                Label(new Rect(row.x + 50, row.y, row.width - 130, row.height), ResultName(recipe), body, unlocked ? Text : Muted);
                var tag = !unlocked ? Loc.T("未解锁") : ready ? Loc.T("可制作") : Loc.T("缺材料");
                Label(new Rect(row.xMax - 80, row.y, 70, row.height), tag, small, ready ? Ready : Muted, TextAnchor.MiddleRight);

                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) Select(i);
                y += RowHeight;
            }

            GUI.EndScrollView();
        }

        private void DrawDetail(Rect area, Inventory bag)
        {
            var recipe = Recipes[Selected];
            var unlocked = authority.IsRecipeUnlocked(recipe);
            var x = area.x + 8;
            var y = area.y + 4;

            Icon(new Rect(x, y, 64, 64), recipe.result, unlocked);
            Label(new Rect(x + 80, y + 4, area.width - 96, 30), ResultName(recipe), title, Text);
            var tagLine = string.IsNullOrEmpty(recipe.category) ? string.Empty : recipe.category;
            if (recipe.result != null && recipe.result.maxStack > 1) tagLine += Loc.F("    身上 {0}", bag.CountOf(recipe.result));
            Label(new Rect(x + 80, y + 38, area.width - 96, 20), tagLine, small, Muted);
            y += 80;

            var description = Loc.T(recipe.description);
            var descHeight = body.CalcHeight(new GUIContent(description), area.width - 16);
            Label(new Rect(x, y, area.width - 16, descHeight), description, body, Muted);
            y += descHeight + 16;

            Label(new Rect(x, y, 200, 20), Loc.T("需要"), small, Ember);
            y += 24;
            foreach (var ingredient in recipe.ingredients)
            {
                if (ingredient.item == null) continue;
                var have = bag.CountOf(ingredient.item);
                Fill(new Rect(x, y, area.width - 16, 34), PanelInner);
                Icon(new Rect(x + 8, y + 6, 22, 22), ingredient.item, true);
                Label(new Rect(x + 40, y, area.width - 160, 34), ingredient.item.DisplayName, body, Text);
                Label(new Rect(x + area.width - 136, y, 112, 34), $"{have} / {ingredient.amount}", body, have >= ingredient.amount ? Ready : Missing, TextAnchor.MiddleRight);
                y += 38;
            }

            var buttonRect = new Rect(area.xMax - 208, area.yMax - 52, 200, 46);
            if (!unlocked)
            {
                Label(new Rect(x, area.yMax - 52, area.width - 232, 46), UnlockText(recipe), body, Ember, TextAnchor.MiddleLeft);
            }
            else if (Time.unscaledTime - feedbackTime < 2.5f)
            {
                Label(new Rect(x, area.yMax - 52, area.width - 232, 46), feedback, body, feedbackColor, TextAnchor.MiddleLeft);
            }

            var ready = unlocked && Crafting.HasIngredients(bag, recipe);
            Fill(buttonRect, ready ? Ember : new Color(0.25f, 0.22f, 0.19f, 1f));
            button.normal.textColor = ready ? new Color(0.12f, 0.07f, 0.03f) : Muted;
            var previous = GUI.enabled;
            GUI.enabled = ready;
            if (GUI.Button(buttonRect, ready ? Loc.T("制作  [F]") : unlocked ? Loc.T("材料不够") : Loc.T("未解锁"), button)) CraftSelected();
            GUI.enabled = previous;
        }

        private string UnlockText(CraftingRecipe recipe)
        {
            var quests = authority.QuestDirector;
            var title = quests != null ? quests.TitleOf(recipe.unlockKey) : string.Empty;
            return string.IsNullOrEmpty(title) ? Loc.F("完成 {0} 后解锁", recipe.unlockKey) : Loc.F("完成 {0}「{1}」后解锁", recipe.unlockKey, title);
        }

        private static string ResultName(CraftingRecipe recipe)
        {
            if (recipe.result == null) return Loc.T("（缺少成品）");
            return recipe.resultAmount > 1 ? $"{recipe.result.DisplayName} ×{recipe.resultAmount}" : recipe.result.DisplayName;
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
            body ??= new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, alignment = TextAnchor.MiddleLeft };
            glyph ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0) };
            small ??= new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            if (button == null)
            {
                button = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                button.normal.textColor = button.hover.textColor = button.active.textColor = new Color(0.12f, 0.07f, 0.03f);
            }
        }

        private void Icon(Rect rect, ItemDefinition item, bool lit)
        {
            Fill(rect, new Color(0f, 0f, 0f, 0.5f));
            if (item != null && item.icon != null)
            {
                var previousColor = GUI.color;
                GUI.color = lit ? Color.white : new Color(0.45f, 0.42f, 0.4f, 1f);
                GUI.DrawTexture(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), item.icon, ScaleMode.ScaleToFit, true);
                GUI.color = previousColor;
                return;
            }

            var color = item != null ? item.iconColor : Color.gray;
            if (!lit) color = Color.Lerp(color, new Color(0.2f, 0.18f, 0.16f), 0.75f);
            Fill(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), color);
            if (item == null || string.IsNullOrEmpty(item.DisplayName)) return;
            // 还没有图标的物品：色块上写物品名的首字，方便区分颜色相近的物品。
            glyph.fontSize = Mathf.RoundToInt(rect.height * 0.55f);
            var ink = color.grayscale > 0.55f ? new Color(0.1f, 0.08f, 0.06f, 0.9f) : new Color(1f, 0.97f, 0.9f, 0.95f);
            Label(rect, item.DisplayName.Substring(0, 1), glyph, ink, TextAnchor.MiddleCenter);
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
