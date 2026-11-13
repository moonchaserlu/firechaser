using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// 庇护所工作台的基础配方。资源只在缺失时创建，之后由策划直接改 asset 调整配方。
    /// 每条配方挂在一个任务完成之后（unlockKey = 任务 ID），保证 Q01 燃料、Q02 零件与电池、Q03 药品不能靠合成跳过；
    /// 原救援循环（非任务模式）里全部可用。
    /// </summary>
    public static class Map01CraftingSetup
    {
        public const string RecipesPath = "Assets/Game/Data/Crafting/CraftingRecipes_Basic.asset";
        private const string GameItemsFolder = "Assets/Game/Data/Items";

        public static CraftingRecipeBook BasicRecipes()
        {
            var book = AssetDatabase.LoadAssetAtPath<CraftingRecipeBook>(RecipesPath);
            if (book != null)
            {
                return book;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Game/Data/Crafting"))
            {
                AssetDatabase.CreateFolder("Assets/Game/Data", "Crafting");
            }

            var cloth = Load("Cloth");
            var chemicals = Load("Chemicals");
            var scrap = Load("ScrapMetal");
            var bandage = Load("DirtyBandage");
            var emptyBattery = AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Map01/Data/Items/Item_EmptyBattery.asset");
            book = ScriptableObject.CreateInstance<CraftingRecipeBook>();
            book.recipes = new List<CraftingRecipe>
            {
                Recipe("fuel_pack", "火", "Q01", Load("FuelPack"), 1,
                    "布料浸透化学品，卷紧压实，能烧很久。炉火保住以后，才腾得出手给自己多备一份。",
                    (cloth, 2), (chemicals, 1)),
                Recipe("empty_battery", "光", "Q02", emptyBattery, 1,
                    "撬开旧电池，换上新触片，还能再充一次电。庇护所的灯亮过一回，就知道这东西有多值钱。",
                    (Load("OldBattery"), 1), (scrap, 1)),
                Recipe("mechanical_parts", "修理", "Q02", Load("MechanicalParts"), 1,
                    "照着修发电机时拆下来的样子，把废铁敲成能用的零件。",
                    (scrap, 3)),
                Recipe("dirty_bandage", "药", "Q03", bandage, 1,
                    "撕成条的布，算不上干净，但能先把血止住。",
                    (cloth, 2)),
                Recipe("first_aid_spray", "药", "Q03", Load("FirstAidSpray"), 1,
                    "把化学品兑进空喷壶，照着前人留下的方子配。比绷带管用，也更难凑齐。",
                    (bandage, 1), (chemicals, 2)),
                Recipe("ammo_9mm", "弹药", "Q06", Load("Ammo9mm"), 6,
                    "拿捡来的弹壳复装，火药不多，凑合能打。过了桥，路上的东西不会只靠躲。",
                    (scrap, 2), (chemicals, 1))
            };
            AssetDatabase.CreateAsset(book, RecipesPath);
            AssetDatabase.SaveAssets();
            return book;
        }

        private static CraftingRecipe Recipe(string id, string category, string unlockQuest, ItemDefinition result, int amount, string description,
            params (ItemDefinition item, int amount)[] ingredients)
        {
            var list = new CraftingIngredient[ingredients.Length];
            for (var i = 0; i < ingredients.Length; i++) list[i] = new CraftingIngredient(ingredients[i].item, ingredients[i].amount);
            return new CraftingRecipe
            {
                recipeId = id, category = category, unlockKey = unlockQuest, description = description,
                result = result, resultAmount = amount, ingredients = list
            };
        }

        private static ItemDefinition Load(string file)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{GameItemsFolder}/Item_{file}.asset");
            if (item == null) Debug.LogError($"合成配方缺少物品：Item_{file}");
            return item;
        }
    }
}
