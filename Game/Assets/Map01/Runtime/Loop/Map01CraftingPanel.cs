using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 工作台合成面板（IMGUI）。在工作台按 E 打开，世界暂停；左侧按分组列出配方，右侧显示说明、材料与制作按钮。
    /// W/S 或 ↑/↓ 选择，E / Enter 制作，Esc 离开；也可以用鼠标点。合成请求照常走权威层。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Map01CraftingPanel : MonoBehaviour
    {
        private const float RowHeight = 46f;
        private const float GroupHeight = 24f;

        private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color PanelColor = new Color(0.085f, 0.07f, 0.058f, 0.97f);
        private static readonly Color PanelInner = new Color(0.12f, 0.1f, 0.082f, 1f);
        private static readonly Color Ember = new Color(0.95f, 0.62f, 0.28f, 1f);
        private static readonly Color EmberDim = new Color(0.55f, 0.36f, 0.18f, 1f);
        private static readonly Color RowSelected = new Color(0.32f, 0.2f, 0.1f, 1f);
        private static readonly Color RowHover = new Color(0.18f, 0.14f, 0.1f, 1f);
        private static readonly Color Ready = new Color(0.56f, 0.88f, 0.52f, 1f);
        private static readonly Color Missing = new Color(1f, 0.47f, 0.4f, 1f);
        private static readonly Color Muted = new Color(0.62f, 0.58f, 0.52f, 1f);
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

        public int Selected { get; private set; }
        public bool IsOpen => flow != null && flow.IsCrafting;

        private IReadOnlyList<CraftingRecipe> Recipes => authority.Recipes;

        private void Start()
        {
            if (authority == null) authority = FindAnyObjectByType<Map01Authority>();
            if (flow == null) flow = FindAnyObjectByType<Map01RunFlow>();
            if (interactor == null) interactor = FindAnyObjectByType<Map01Interactor>();
        }

        private void Update()
        {
            // 打开面板的那一下 E 不算制作：面板至少开过一帧才接收按键。
            var open = IsOpen;
            var accept = open && openLastFrame;
            openLastFrame = open;
            if (!accept || Recipes.Count == 0) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame) Select(Selected - 1);
            if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame) Select(Selected + 1);
            if (keyboard.eKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) CraftSelected();
        }

        public void Select(int index)
        {
            if (Recipes.Count == 0) return;
            Selected = Mathf.Clamp(index, 0, Recipes.Count - 1);
        }

        /// <summary>制作当前选中的配方。</summary>
        public InteractionResult CraftSelected()
        {
            if (!IsOpen || Recipes.Count == 0) return InteractionResult.InvalidState;
            var recipe = Recipes[Selected];
            var result = interactor.SendTo(flow.CraftingStation, InteractionAction.Craft, Selected);
            feedbackTime = Time.unscaledTime;
            if (result == InteractionResult.Accepted)
            {
                feedback = $"做好了：{ResultName(recipe)}";
                feedbackColor = Ready;
            }
            else
            {
                feedback = result switch
                {
                    InteractionResult.Locked => "还没学会这个做法",
                    InteractionResult.MissingItem => "材料不够",
                    InteractionResult.InventoryFull => "背包放不下",
                    _ => "现在做不了"
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

            Fill(new Rect(0, 0, Screen.width, Screen.height), Backdrop);
            var width = Mathf.Min(860f, Screen.width - 32f);
            var height = Mathf.Min(520f, Screen.height - 32f);
            var frame = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            Fill(new Rect(frame.x - 2, frame.y - 2, frame.width + 4, frame.height + 4), EmberDim);
            Fill(frame, PanelColor);

            var bag = authority.Player.Inventory.Inventory;
            Label(new Rect(frame.x + 24, frame.y + 16, 400, 30), "庇护所工作台", title, Ember);
            Label(new Rect(frame.x + 24, frame.y + 48, 520, 20), "借着炉火的光，把捡回来的东西拼成能用的。", small, Muted);
            var info = $"背包 {bag.UsedSlots}/{bag.Capacity} 格      [Esc] 离开";
            Label(new Rect(frame.xMax - 324, frame.y + 22, 300, 22), info, body, Muted, TextAnchor.MiddleRight);
            Fill(new Rect(frame.x + 20, frame.y + 76, frame.width - 40, 1), EmberDim);

            var content = new Rect(frame.x + 20, frame.y + 88, frame.width - 40, frame.height - 108);
            if (Recipes.Count == 0)
            {
                Label(content, "这张工作台上还没有能做的东西。", body, Muted, TextAnchor.MiddleCenter);
                return;
            }

            Selected = Mathf.Clamp(Selected, 0, Recipes.Count - 1);
            var listWidth = Mathf.Min(300f, content.width * 0.4f);
            DrawList(new Rect(content.x, content.y, listWidth, content.height), bag);
            DrawDetail(new Rect(content.x + listWidth + 16, content.y, content.width - listWidth - 16, content.height), bag);
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
                    Label(new Rect(12, y + 4, view.width - 24, 18), string.IsNullOrEmpty(group) ? "其他" : group, small, Ember);
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
                var tag = !unlocked ? "未解锁" : ready ? "可制作" : "缺材料";
                Label(new Rect(row.xMax - 80, row.y, 70, row.height), tag, small, !unlocked ? EmberDim : ready ? Ready : Muted, TextAnchor.MiddleRight);

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
            if (recipe.result != null && recipe.result.maxStack > 1) tagLine += $"    身上 {bag.CountOf(recipe.result)}";
            Label(new Rect(x + 80, y + 38, area.width - 96, 20), tagLine, small, Muted);
            y += 80;

            var descHeight = body.CalcHeight(new GUIContent(recipe.description), area.width - 16);
            Label(new Rect(x, y, area.width - 16, descHeight), recipe.description, body, Muted);
            y += descHeight + 16;

            Label(new Rect(x, y, 200, 20), "需要", small, Ember);
            y += 24;
            foreach (var ingredient in recipe.ingredients)
            {
                if (ingredient.item == null) continue;
                var have = bag.CountOf(ingredient.item);
                Fill(new Rect(x, y, area.width - 16, 34), PanelInner);
                Icon(new Rect(x + 8, y + 6, 22, 22), ingredient.item, true);
                Label(new Rect(x + 40, y, area.width - 160, 34), ingredient.item.displayName, body, Text);
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
            if (GUI.Button(buttonRect, ready ? "制作  [E]" : unlocked ? "材料不够" : "未解锁", button)) CraftSelected();
            GUI.enabled = previous;
        }

        private string UnlockText(CraftingRecipe recipe)
        {
            var quests = authority.QuestDirector;
            var title = quests != null ? quests.TitleOf(recipe.unlockKey) : string.Empty;
            return string.IsNullOrEmpty(title) ? $"完成 {recipe.unlockKey} 后解锁" : $"完成 {recipe.unlockKey}「{title}」后解锁";
        }

        private static string ResultName(CraftingRecipe recipe)
        {
            if (recipe.result == null) return "（缺少成品）";
            return recipe.resultAmount > 1 ? $"{recipe.result.displayName} ×{recipe.resultAmount}" : recipe.result.displayName;
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
            var color = item != null ? item.iconColor : Color.gray;
            if (!lit) color = Color.Lerp(color, new Color(0.2f, 0.18f, 0.16f), 0.75f);
            Fill(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), color);
            if (item == null || string.IsNullOrEmpty(item.displayName)) return;
            // 占位图标：物品色块上写物品名的首字，方便区分颜色相近的物品。
            glyph.fontSize = Mathf.RoundToInt(rect.height * 0.55f);
            var ink = color.grayscale > 0.55f ? new Color(0.1f, 0.08f, 0.06f, 0.9f) : new Color(1f, 0.97f, 0.9f, 0.95f);
            Label(rect, item.displayName.Substring(0, 1), glyph, ink, TextAnchor.MiddleCenter);
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
