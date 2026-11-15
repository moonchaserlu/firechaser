using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    [Serializable]
    public struct CraftingIngredient
    {
        public ItemDefinition item;
        [Min(1)] public int amount;

        public CraftingIngredient(ItemDefinition item, int amount)
        {
            this.item = item;
            this.amount = amount;
        }
    }

    /// <summary>一条配方：消耗若干材料，产出一种物品。</summary>
    [Serializable]
    public sealed class CraftingRecipe
    {
        public string recipeId = "recipe";
        [Tooltip("界面上的分组，例如 火 / 光 / 药 / 修理 / 弹药。")]
        public string category = string.Empty;
        [TextArea(2, 4)] public string description = string.Empty;
        [Tooltip("解锁条件的键（Map01 中为任务 ID，完成该任务后解锁）；留空表示一开始就能做。")]
        public string unlockKey = string.Empty;
        public CraftingIngredient[] ingredients = Array.Empty<CraftingIngredient>();
        public ItemDefinition result;
        [Min(1)] public int resultAmount = 1;
    }

    /// <summary>某个工作台可用的配方列表；顺序即界面上的排列顺序。</summary>
    [CreateAssetMenu(menuName = "Wilderness/Crafting Recipe Book", fileName = "CraftingRecipes")]
    public sealed class CraftingRecipeBook : ScriptableObject
    {
        public List<CraftingRecipe> recipes = new List<CraftingRecipe>();
    }
}
