using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>
    /// 庇护所工作台：配方随任务解锁，不能用合成跳过 Q01–Q03；合成走权威层校验；合成面板打开时世界暂停。
    /// </summary>
    public sealed class Map01CraftingTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";

        private Map01Authority authority;
        private PlayerCharacter player;
        private int sequence = 100;

        private Inventory Bag => player.Inventory.Inventory;

        private IEnumerator Load(bool questMode)
        {
            Map01Authority.SeedOverride = 20261023;
            Map01Authority.SuppressMonsters = true;
            Map01Authority.QuestModeOverride = questMode;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("只在编辑器中运行。");
#endif
            yield return null;
            authority = Object.FindAnyObjectByType<Map01Authority>();
            player = authority.Player;
            player.Exposure.enabled = false;
        }

        [TearDown]
        public void Restore()
        {
            Map01Authority.SeedOverride = null;
            Map01Authority.SuppressMonsters = false;
            Map01Authority.QuestModeOverride = null;
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator Workbench_InsideHearthLight_WithQuestGatedRecipes()
        {
            yield return Load(true);
            var bench = authority.FindStation(Map01Station.Workbench);
            Assert.IsNotNull(bench, "庇护所有工作台");
            var hearth = authority.FindStation(Map01Station.Hearth);
            Assert.Less(bench.HorizontalDistanceTo(hearth.transform.position), authority.QuestDirector.Chain.hearthRadius - 0.5f,
                "工作台在开局唯一的炉火光圈里，Q02 之前也能安全使用");

            var recipes = authority.Recipes;
            Assert.GreaterOrEqual(recipes.Count, 5);
            Assert.IsTrue(recipes.All(r => r.result != null && r.ingredients.Length > 0 && r.ingredients.All(i => i.item != null)), "配方引用的物品都存在");
            Assert.IsTrue(recipes.All(r => !string.IsNullOrEmpty(r.description) && !string.IsNullOrEmpty(r.category)), "每条配方有分组和说明");
            Assert.IsNotNull(Find("fuel_pack").result.icon, "燃料包已绑定图标");
            Assert.AreEqual("Q01", Find("fuel_pack").unlockKey, "燃料在 Q01 后");
            Assert.AreEqual("Q02", Find("mechanical_parts").unlockKey, "零件在 Q02 后");
            Assert.AreEqual("Q02", Find("empty_battery").unlockKey, "空电池在 Q02 后");
            Assert.IsTrue(recipes.Where(r => (r.result.tags & ItemTags.Medical) != 0).All(r => r.unlockKey == "Q03"), "药品在 Q03 后");
            Assert.IsTrue(recipes.All(r => !authority.IsRecipeUnlocked(r)), "开局全部未解锁");
        }

        [UnityTest]
        public IEnumerator QuestMode_RecipesUnlockAfterTheirQuest()
        {
            yield return Load(true);
            var fuel = Find("fuel_pack");
            var fuelIndex = IndexOf("fuel_pack");
            Bag.TakeAll();
            AddIngredients(fuel);

            yield return GoTo(Map01Station.Workbench);
            Assert.AreEqual(InteractionResult.Locked, Send(Map01Station.Workbench, InteractionAction.Craft, fuelIndex), "Q01 前不能合成燃料");
            Assert.AreEqual(0, Bag.CountOf(fuel.result));
            Assert.IsTrue(fuel.ingredients.All(i => Bag.CountOf(i.item) == i.amount), "被拒绝时不扣材料");

            // 完成 Q01：带一份燃料去炉火提交。
            Bag.TryAdd(fuel.result);
            yield return GoTo(Map01Station.Hearth);
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.Hearth, InteractionAction.Quest));
            Assert.IsTrue(authority.QuestDirector.IsCompleted("Q01"));
            Assert.IsTrue(authority.IsRecipeUnlocked(fuel));
            Assert.IsFalse(authority.IsRecipeUnlocked(Find("mechanical_parts")), "Q02 的配方仍锁着");

            yield return GoTo(Map01Station.Workbench);
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.Workbench, InteractionAction.Craft, fuelIndex));
            Assert.AreEqual(1, Bag.CountOf(fuel.result));
        }

        [UnityTest]
        public IEnumerator RescueLoop_AllRecipesOpen_AndRequestsValidated()
        {
            yield return Load(false);
            var index = IndexOf("fuel_pack");
            var recipe = authority.Recipes[index];
            Assert.IsTrue(authority.Recipes.All(authority.IsRecipeUnlocked), "原救援循环里全部可用");
            Bag.TakeAll();

            yield return GoTo(Map01Station.ShopTerminal);
            Assert.AreEqual(InteractionResult.WrongStation, Send(Map01Station.ShopTerminal, InteractionAction.Craft, index), "商店不能合成");
            yield return GoTo(Map01Station.Workbench);
            Assert.AreEqual(InteractionResult.MissingItem, Send(Map01Station.Workbench, InteractionAction.Craft, index));
            Assert.AreEqual(InteractionResult.UnknownEntity, Send(Map01Station.Workbench, InteractionAction.Craft, 99));

            AddIngredients(recipe);
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.Workbench, InteractionAction.Craft, index));
            Assert.AreEqual(recipe.resultAmount, Bag.CountOf(recipe.result));
            foreach (var ingredient in recipe.ingredients) Assert.AreEqual(0, Bag.CountOf(ingredient.item));
        }

        [UnityTest]
        public IEnumerator Panel_PausesWorld_AndCraftsSelectedRecipe()
        {
            yield return Load(false);
            var flow = Object.FindAnyObjectByType<Map01RunFlow>();
            var panel = Object.FindAnyObjectByType<Map01CraftingPanel>();
            Assert.IsNotNull(panel, "场景里有合成面板");
            var index = IndexOf("mechanical_parts");
            var recipe = authority.Recipes[index];
            Bag.TakeAll();
            AddIngredients(recipe);

            yield return GoTo(Map01Station.Workbench);
            flow.OpenCrafting(authority.FindStation(Map01Station.Workbench));
            Assert.IsTrue(panel.IsOpen);
            Assert.AreEqual(0f, Time.timeScale, "使用工作台时世界暂停");
            Assert.IsFalse(player.Input.GameplayEnabled, "使用工作台时不能移动射击");

            flow.SetPaused(true);
            Assert.IsFalse(flow.IsPaused, "工作台打开时不叠加暂停菜单");

            panel.Select(index);
            Assert.AreEqual(InteractionResult.Accepted, panel.CraftSelected());
            Assert.AreEqual(1, Bag.CountOf(recipe.result));
            Assert.AreEqual(InteractionResult.MissingItem, panel.CraftSelected(), "材料用完后再做被拒绝");

            flow.CloseCrafting();
            Assert.IsFalse(panel.IsOpen);
            Assert.AreEqual(1f, Time.timeScale);
            yield return null;
            yield return null;
            Assert.IsTrue(player.Input.GameplayEnabled, "离开工作台后恢复操作");
        }

        [UnityTest]
        public IEnumerator Backpack_CraftsInTheField_WithoutPausing()
        {
            yield return Load(false);
            var flow = Object.FindAnyObjectByType<Map01RunFlow>();
            var panel = Object.FindAnyObjectByType<Map01CraftingPanel>();
            var index = IndexOf("mechanical_parts");
            var recipe = authority.Recipes[index];
            Bag.TakeAll();
            AddIngredients(recipe);

            yield return GoTo(Map01Station.Generator);
            var bench = authority.FindStation(Map01Station.Workbench);
            Assert.Greater(bench.HorizontalDistanceTo(player.transform.position), authority.InteractRange + 1f, "远离工作台");

            flow.OpenBackpack();
            Assert.IsTrue(panel.IsOpen);
            Assert.AreEqual(1f, Time.timeScale, "背包打开时世界不暂停");
            Assert.IsFalse(player.Input.GameplayEnabled, "背包打开时不能移动射击");
            yield return null;
            Assert.AreEqual(CursorLockMode.None, Cursor.lockState, "背包打开时解锁鼠标");
            Assert.IsTrue(Cursor.visible, "背包打开时显示系统光标");
            Assert.IsFalse(player.GetComponent<PlayerStatusHud>().CrosshairActive, "背包打开时隐藏准星");
            flow.SetPaused(true);
            Assert.IsFalse(flow.IsPaused);
            flow.OpenCrafting(bench);
            Assert.IsFalse(flow.IsCrafting, "背包和工作台不同时打开");

            Assert.AreEqual(BackpackPage.Items, panel.Page, "打开背包先看到物品");
            Assert.AreEqual(InteractionResult.InvalidState, panel.CraftSelected(), "物品页不能制作");
            Assert.AreEqual(0, Bag.CountOf(recipe.result));

            panel.Page = BackpackPage.Crafting;
            panel.Select(index);
            Assert.AreEqual(InteractionResult.Accepted, panel.CraftSelected());
            Assert.AreEqual(1, Bag.CountOf(recipe.result));
            Assert.AreEqual(InteractionResult.MissingItem, panel.CraftSelected());

            flow.CloseBackpack();
            Assert.IsFalse(panel.IsOpen);
            yield return null;
            yield return null;
            Assert.IsTrue(player.Input.GameplayEnabled, "关上背包后恢复操作");

            flow.OpenBackpack();
            Assert.AreEqual(BackpackPage.Items, panel.Page, "重新打开回到物品页");
            flow.CloseBackpack();
        }

        [UnityTest]
        public IEnumerator Backpack_RespectsQuestUnlocks()
        {
            yield return Load(true);
            var flow = Object.FindAnyObjectByType<Map01RunFlow>();
            var panel = Object.FindAnyObjectByType<Map01CraftingPanel>();
            var fuel = Find("fuel_pack");
            Bag.TakeAll();
            AddIngredients(fuel);

            flow.OpenBackpack();
            panel.Page = BackpackPage.Crafting;
            panel.Select(IndexOf("fuel_pack"));
            Assert.AreEqual(InteractionResult.Locked, panel.CraftSelected(), "野外也不能跳过 Q01");
            Assert.AreEqual(0, Bag.CountOf(fuel.result));
            flow.CloseBackpack();
        }

        [UnityTest]
        public IEnumerator FieldEntity_OnlyAllowsCrafting()
        {
            yield return Load(false);
            Assert.AreEqual(InteractionResult.UnknownEntity,
                authority.Request(new InteractionRequest(Map01Authority.FieldCraftEntity, InteractionAction.Buy, ++sequence)), "野外入口不能用来远程购买");
        }

        private CraftingRecipe Find(string id) => authority.Recipes.First(r => r.recipeId == id);

        private int IndexOf(string id) => Enumerable.Range(0, authority.Recipes.Count).First(i => authority.Recipes[i].recipeId == id);

        private void AddIngredients(CraftingRecipe recipe)
        {
            foreach (var ingredient in recipe.ingredients)
            {
                for (var i = 0; i < ingredient.amount; i++) Assert.IsTrue(Bag.TryAdd(ingredient.item));
            }
        }

        private IEnumerator GoTo(Map01Station station)
        {
            var target = authority.FindStation(station);
            var away = player.transform.position - target.transform.position;
            away.y = 0f;
            var offset = away.sqrMagnitude > 0.01f ? away.normalized * 1.2f : Vector3.forward * 1.2f;
            player.Motor.Teleport(target.transform.position + offset + Vector3.up * 0.1f);
            yield return null;
        }

        private InteractionResult Send(Map01Station station, InteractionAction action, int argument = 0)
        {
            return authority.Request(new InteractionRequest(authority.FindStation(station).EntityId, action, ++sequence, argument));
        }
    }
}
