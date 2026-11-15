using NUnit.Framework;

namespace WildernessExtraction.Tests
{
    public sealed class CraftingTests
    {
        private TestItems items;
        private ItemDefinition cloth;
        private ItemDefinition chemicals;
        private ItemDefinition fuel;

        [SetUp]
        public void SetUp()
        {
            items = new TestItems();
            cloth = Stackable(items.Create("cloth", 1), 5);
            chemicals = Stackable(items.Create("chemicals", 2), 3);
            fuel = Stackable(items.Create("fuel_pack", 3), 5);
        }

        [TearDown]
        public void TearDown() => items.DestroyAll();

        [Test]
        public void TryCraft_ConsumesIngredientsAndAddsResult()
        {
            var bag = Bag(4, cloth, cloth, chemicals);
            var recipe = Recipe(fuel, 1, (cloth, 1), (chemicals, 1));

            Assert.IsTrue(Crafting.HasIngredients(bag, recipe));
            Assert.AreEqual(CraftResult.Crafted, Crafting.TryCraft(bag, recipe));
            Assert.AreEqual(1, bag.CountOf(cloth));
            Assert.AreEqual(0, bag.CountOf(chemicals));
            Assert.AreEqual(1, bag.CountOf(fuel));
        }

        [Test]
        public void TryCraft_MissingIngredient_ChangesNothing()
        {
            var bag = Bag(4, cloth);
            var recipe = Recipe(fuel, 1, (cloth, 1), (chemicals, 1));

            Assert.IsFalse(Crafting.HasIngredients(bag, recipe));
            Assert.AreEqual(CraftResult.MissingIngredients, Crafting.TryCraft(bag, recipe));
            Assert.AreEqual(1, bag.CountOf(cloth));
            Assert.AreEqual(0, bag.CountOf(fuel));
        }

        [Test]
        public void TryCraft_DuplicateIngredientRows_AreSummed()
        {
            var bag = Bag(4, cloth);
            var recipe = Recipe(fuel, 1, (cloth, 1), (cloth, 1));

            Assert.AreEqual(CraftResult.MissingIngredients, Crafting.TryCraft(bag, recipe));
        }

        [Test]
        public void TryCraft_NoRoomForResult_RollsBack()
        {
            // 背包两格已满：消耗一格材料后，两格大的成品或两份不可堆叠的成品都放不下。
            var big = items.Create("big", 1);
            big.size = 2;
            fuel.maxStack = 1;
            var bag = Bag(2, cloth, chemicals);

            Assert.AreEqual(CraftResult.InventoryFull, Crafting.TryCraft(bag, Recipe(big, 1, (cloth, 1))));
            Assert.AreEqual(CraftResult.InventoryFull, Crafting.TryCraft(bag, Recipe(fuel, 2, (cloth, 1))));
            Assert.AreEqual(1, bag.CountOf(cloth));
            Assert.AreEqual(1, bag.CountOf(chemicals));
            Assert.AreEqual(0, bag.CountOf(big));
            Assert.AreEqual(0, bag.CountOf(fuel));
        }

        [Test]
        public void TryCraft_InvalidRecipe_IsRejected()
        {
            var bag = Bag(4, cloth);
            Assert.AreEqual(CraftResult.InvalidRecipe, Crafting.TryCraft(bag, null));
            Assert.AreEqual(CraftResult.InvalidRecipe, Crafting.TryCraft(bag, Recipe(null, 1, (cloth, 1))));
            Assert.AreEqual(CraftResult.InvalidRecipe, Crafting.TryCraft(bag, Recipe(fuel, 1)));
        }

        private static ItemDefinition Stackable(ItemDefinition item, int maxStack)
        {
            item.maxStack = maxStack;
            return item;
        }

        private static Inventory Bag(int capacity, params ItemDefinition[] contents)
        {
            var bag = new Inventory(capacity);
            foreach (var item in contents) Assert.IsTrue(bag.TryAdd(item));
            return bag;
        }

        private static CraftingRecipe Recipe(ItemDefinition result, int amount, params (ItemDefinition item, int amount)[] ingredients)
        {
            var list = new CraftingIngredient[ingredients.Length];
            for (var i = 0; i < ingredients.Length; i++) list[i] = new CraftingIngredient(ingredients[i].item, ingredients[i].amount);
            return new CraftingRecipe { recipeId = "test", result = result, resultAmount = amount, ingredients = list };
        }
    }
}
