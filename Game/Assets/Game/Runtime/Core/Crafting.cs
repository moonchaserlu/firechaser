namespace WildernessExtraction
{
    public enum CraftResult { Crafted, InvalidRecipe, MissingIngredients, InventoryFull }

    /// <summary>合成规则：材料够就扣材料、放成品；放不下时整笔回滚，背包保持原样。</summary>
    public static class Crafting
    {
        public static bool HasIngredients(Inventory inventory, CraftingRecipe recipe)
        {
            if (!IsValid(recipe)) return false;
            foreach (var ingredient in recipe.ingredients)
            {
                if (inventory.CountOf(ingredient.item) < Required(recipe, ingredient.item)) return false;
            }

            return true;
        }

        public static CraftResult TryCraft(Inventory inventory, CraftingRecipe recipe)
        {
            if (!IsValid(recipe)) return CraftResult.InvalidRecipe;
            if (!HasIngredients(inventory, recipe)) return CraftResult.MissingIngredients;

            foreach (var ingredient in recipe.ingredients)
            {
                inventory.Remove(ingredient.item, ingredient.amount);
            }

            var added = 0;
            while (added < recipe.resultAmount && inventory.TryAdd(recipe.result))
            {
                added++;
            }

            if (added == recipe.resultAmount) return CraftResult.Crafted;

            inventory.Remove(recipe.result, added);
            foreach (var ingredient in recipe.ingredients)
            {
                for (var i = 0; i < ingredient.amount; i++) inventory.TryAdd(ingredient.item);
            }

            return CraftResult.InventoryFull;
        }

        private static bool IsValid(CraftingRecipe recipe)
        {
            if (recipe == null || recipe.result == null || recipe.resultAmount <= 0 || recipe.ingredients == null || recipe.ingredients.Length == 0) return false;
            foreach (var ingredient in recipe.ingredients)
            {
                if (ingredient.item == null || ingredient.amount <= 0) return false;
            }

            return true;
        }

        /// <summary>同一种材料在配方里写了多行时合计需求。</summary>
        private static int Required(CraftingRecipe recipe, ItemDefinition item)
        {
            var total = 0;
            foreach (var ingredient in recipe.ingredients)
            {
                if (ingredient.item == item) total += ingredient.amount;
            }

            return total;
        }
    }
}
