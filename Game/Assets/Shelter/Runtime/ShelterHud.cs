using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WildernessExtraction.Shelter
{
    /// <summary>安全屋占位界面（IMGUI）：状态栏、设施提示、靶场成绩，以及仓库、工作台、出口面板。</summary>
    public sealed class ShelterHud : MonoBehaviour
    {
        [SerializeField] private ShelterController controller;
        [SerializeField] private ShelterRange range;

        private GUIStyle box;
        private GUIStyle title;
        private Vector2 leftScroll;
        private Vector2 rightScroll;
        private float messageTime;
        private string message;

        private void Start()
        {
            if (controller == null) controller = FindAnyObjectByType<ShelterController>();
            if (range == null) range = FindAnyObjectByType<ShelterRange>();
            controller.Message += text =>
            {
                message = text;
                messageTime = Time.unscaledTime;
            };
            if (!string.IsNullOrEmpty(controller.LastMessage))
            {
                message = controller.LastMessage;
                messageTime = Time.unscaledTime;
            }
        }

        private void OnGUI()
        {
            if (controller == null || controller.Profile == null) return;
            box ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15, richText = true, padding = new RectOffset(10, 10, 8, 8) };
            title ??= new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };

            var profile = controller.Profile;
            var bag = controller.Bag;
            GUI.Box(new Rect(12, 12, 280, 76),
                Loc.F("地下庇护所\n仓库：{0}/{1}　背包：{2}/{3}\n已远行：{4} 次", profile.Stash.UsedSlots, profile.Stash.Capacity, bag.UsedSlots, bag.Capacity, profile.CompletedRaids), box);

            if (range != null && range.PlayerInside)
            {
                var stats = new StringBuilder(Loc.T("靶场（练习弹不消耗，弹匣打空要换弹）\n"));
                for (var i = 0; i < range.Targets.Count; i++)
                {
                    var target = range.Targets[i];
                    if (target == null) continue;
                    stats.AppendLine(Loc.F("靶 {0}：命中 {1}　最近一发 {2:F0}", i + 1, target.Hits, target.LastDamage));
                }

                GUI.Box(new Rect(Screen.width - 312, 12, 300, 110), stats.ToString(), box);
                if (GUI.Button(new Rect(Screen.width - 312, 126, 120, 28), Loc.T("清零成绩"))) range.ResetStats();
            }

            if (!string.IsNullOrEmpty(message) && Time.unscaledTime - messageTime < 5f)
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 260, 16, 520, 34), message, box);
            }

            var panel = controller.OpenPanel;
            if (panel == null)
            {
                if (controller.Focus != null)
                {
                    GUI.Box(new Rect(Screen.width * 0.5f - 200, Screen.height - 120, 400, 34), $"[F] {controller.Focus.DisplayName}", box);
                }

                return;
            }

            var area = new Rect(Screen.width * 0.5f - 380, Screen.height * 0.5f - 230, 760, 460);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 14, area.y + 10, area.width - 28, area.height - 20));
            GUILayout.BeginHorizontal();
            GUILayout.Label(panel.DisplayName, title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("关闭（Esc）"), GUILayout.Width(110))) controller.ClosePanel();
            GUILayout.EndHorizontal();

            switch (panel.Kind)
            {
                case ShelterStationKind.Stash:
                    DrawStash(profile, bag);
                    break;
                case ShelterStationKind.Workbench:
                    DrawWorkbench(bag);
                    break;
                case ShelterStationKind.Exit:
                    DrawExit(bag);
                    break;
                default:
                    break;
            }

            GUILayout.EndArea();
        }

        private void DrawStash(PlayerProfile profile, Inventory bag)
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(350));
            GUILayout.Label(Loc.F("仓库 {0}/{1}（点击放进背包）", profile.Stash.UsedSlots, profile.Stash.Capacity));
            leftScroll = GUILayout.BeginScrollView(leftScroll, GUILayout.Height(360));
            foreach (var (item, count) in Group(profile.Stash))
            {
                if (GUILayout.Button($"{item.DisplayName} ×{count}")) controller.MoveToBackpack(item);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.Width(350));
            GUILayout.Label(Loc.F("背包 {0}/{1}（点击放回仓库）", bag.UsedSlots, bag.Capacity));
            rightScroll = GUILayout.BeginScrollView(rightScroll, GUILayout.Height(360));
            foreach (var (item, count) in Group(bag))
            {
                if (GUILayout.Button($"{item.DisplayName} ×{count}")) controller.MoveToStash(item);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawWorkbench(Inventory bag)
        {
            GUILayout.Label(Loc.T("用背包里的材料合成"));
            leftScroll = GUILayout.BeginScrollView(leftScroll, GUILayout.Height(370));
            if (controller.Recipes != null)
            {
                foreach (var recipe in controller.Recipes.recipes)
                {
                    if (recipe == null || recipe.result == null) continue;
                    var needs = new StringBuilder();
                    foreach (var ingredient in recipe.ingredients)
                    {
                        if (ingredient.item == null) continue;
                        needs.Append(string.Format("{0} {1}/{2}    ", ingredient.item.DisplayName, bag.CountOf(ingredient.item), ingredient.amount));
                    }

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{recipe.result.DisplayName} ×{recipe.resultAmount}", GUILayout.Width(170));
                    GUILayout.Label(needs.ToString(), GUILayout.Width(420));
                    GUI.enabled = Crafting.HasIngredients(bag, recipe);
                    if (GUILayout.Button(Loc.T("合成"), GUILayout.Width(80))) controller.Craft(recipe);
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();
        }

        private void DrawExit(Inventory bag)
        {
            GUILayout.Label(Loc.T("楼梯通往地面的前哨小屋（Map01）。回到小屋北门外停留几秒即可返回这里。"));
            GUILayout.Label(Loc.T("带上的东西如果死在外面就全没了，留在仓库的才安全。"));
            GUILayout.Space(8);
            GUILayout.Label(bag.ItemCount == 0 ? Loc.T("背包是空的。") : Loc.T("将带上："));
            foreach (var (item, count) in Group(bag))
            {
                GUILayout.Label(Loc.F("　{0} ×{1}", item.DisplayName, count));
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("出发"), GUILayout.Width(200), GUILayout.Height(40))) controller.Depart();
        }

        private static List<(ItemDefinition item, int count)> Group(Inventory inventory)
        {
            var order = new List<ItemDefinition>();
            var counts = new Dictionary<ItemDefinition, int>();
            foreach (var item in inventory.Items)
            {
                if (!counts.ContainsKey(item))
                {
                    counts[item] = 0;
                    order.Add(item);
                }

                counts[item]++;
            }

            var result = new List<(ItemDefinition, int)>();
            foreach (var item in order) result.Add((item, counts[item]));
            return result;
        }
    }
}
