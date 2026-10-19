using System.Linq;
using NUnit.Framework;

namespace WildernessExtraction.Tests
{
    public sealed class LootAndInventoryTests
    {
        private TestItems items;
        private ItemDefinition scrap;
        private ItemDefinition canned;
        private ItemDefinition battery;

        [SetUp]
        public void SetUp()
        {
            items = new TestItems();
            scrap = items.Create("scrap_metal", 1);
            canned = items.Create("canned_food", 2);
            battery = items.Create("old_battery", 4);
        }

        [TearDown]
        public void TearDown()
        {
            items.DestroyAll();
        }

        [Test]
        public void Inventory_AcceptsUpToCapacity_ThenRejects()
        {
            var inventory = new Inventory(8);
            for (var i = 0; i < 8; i++)
            {
                Assert.IsTrue(inventory.TryAdd(scrap));
            }

            Assert.IsTrue(inventory.IsFull);
            Assert.IsFalse(inventory.TryAdd(battery));
            Assert.AreEqual(8, inventory.Count);
            Assert.AreEqual(0, inventory.CountOf(battery));
        }

        [Test]
        public void Inventory_TakeAll_ReturnsItemsAndEmpties()
        {
            var inventory = new Inventory(8);
            inventory.TryAdd(scrap);
            inventory.TryAdd(battery);

            var taken = inventory.TakeAll();
            CollectionAssert.AreEqual(new[] { scrap, battery }, taken);
            Assert.AreEqual(0, inventory.Count);
        }

        [Test]
        public void LootTable_ProducesFixedDropsInOrder()
        {
            var table = items.CreateTable((battery, 2), (scrap, 1));
            var first = table.CreateDrops();
            var second = table.CreateDrops();

            CollectionAssert.AreEqual(new[] { battery, battery, scrap }, first);
            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void LootTable_SkipsNullAndZeroEntries()
        {
            var table = items.CreateTable((null, 3), (canned, 0), (scrap, 1));
            CollectionAssert.AreEqual(new[] { scrap }, table.CreateDrops());
        }

        [Test]
        public void LootPile_TransfersEverythingWhenSpaceAvailable()
        {
            var inventory = new Inventory(8);
            var pile = new LootPile(items.CreateTable((scrap, 2), (canned, 1)).CreateDrops());

            var result = pile.TransferTo(inventory);

            Assert.AreEqual(3, result.Moved);
            Assert.AreEqual(0, result.Left);
            Assert.IsFalse(result.BlockedByFullInventory);
            Assert.IsTrue(pile.IsEmpty);
            Assert.AreEqual(3, inventory.Count);
        }

        [Test]
        public void LootPile_PartialTransferWhenFull_NoDuplicationOrLoss()
        {
            var inventory = new Inventory(8);
            for (var i = 0; i < 6; i++)
            {
                inventory.TryAdd(scrap);
            }

            var pile = new LootPile(items.CreateTable((battery, 2), (canned, 2)).CreateDrops());
            var result = pile.TransferTo(inventory);

            Assert.AreEqual(2, result.Moved);
            Assert.AreEqual(2, result.Left);
            Assert.IsTrue(result.BlockedByFullInventory);
            Assert.AreEqual(8, inventory.Count);
            Assert.AreEqual(2, inventory.CountOf(battery));
            CollectionAssert.AreEqual(new[] { canned, canned }, pile.Remaining.ToArray());

            // 再次尝试仍然满：不移动任何物品。
            var again = pile.TransferTo(inventory);
            Assert.AreEqual(0, again.Moved);
            Assert.AreEqual(2, again.Left);
            Assert.AreEqual(8, inventory.Count);

            // 腾出空间后取回剩余：总数守恒。
            inventory.TakeAll();
            var rest = pile.TransferTo(inventory);
            Assert.AreEqual(2, rest.Moved);
            Assert.IsTrue(pile.IsEmpty);
            Assert.AreEqual(2, inventory.CountOf(canned));
        }

        [Test]
        public void SliceLootTables_TotalExceedsCapacity()
        {
            // 与 VerticalSliceBuilder 中的固定掉落表一致：A=3、B=4、C=3，共 10 件 > 8 格。
            var a = items.CreateTable((scrap, 2), (canned, 1));
            var b = items.CreateTable((canned, 2), (scrap, 1), (battery, 1));
            var c = items.CreateTable((battery, 2), (scrap, 1));

            var inventory = new Inventory(8);
            var piles = new[] { new LootPile(a.CreateDrops()), new LootPile(b.CreateDrops()), new LootPile(c.CreateDrops()) };
            var moved = piles.Sum(pile => pile.TransferTo(inventory).Moved);
            var left = piles.Sum(pile => pile.Remaining.Count);

            Assert.AreEqual(8, moved);
            Assert.AreEqual(2, left);
            Assert.AreEqual(10, moved + left);
        }

        [Test]
        public void Inventory_StacksItems_AndUsesDefinitionSize()
        {
            scrap.maxStack = 5;
            battery.size = 2;
            var inventory = new Inventory(3);
            Assert.IsTrue(inventory.TryAdd(scrap));
            Assert.IsTrue(inventory.TryAdd(scrap));
            Assert.AreEqual(1, inventory.UsedSlots);
            Assert.AreEqual(2, inventory.ItemCount);
            Assert.IsTrue(inventory.TryAdd(battery));
            Assert.AreEqual(3, inventory.UsedSlots);
            Assert.IsFalse(inventory.TryAdd(canned));
        }

        [Test]
        public void WeightedLoot_IsRepeatableForSameSeed()
        {
            var table = items.CreateTable();
            table.randomRolls = 4;
            table.entries.Add(new LootTable.Entry(scrap, 0) { weight = 65f, minCount = 1, maxCount = 1 });
            table.entries.Add(new LootTable.Entry(battery, 0) { weight = 7f, minCount = 1, maxCount = 1 });
            CollectionAssert.AreEqual(table.CreateDrops(new System.Random(1234)), table.CreateDrops(new System.Random(1234)));
        }

        [Test]
        public void QuickSlots_UsePlayerAssignedItems()
        {
            var bar = new QuickSlotBar(8);
            Assert.IsTrue(bar.Assign(5, battery));
            Assert.AreSame(battery, bar[5]);
            Assert.AreEqual(5, bar.SelectedIndex);
            Assert.IsTrue(bar.Select(5));
            Assert.AreSame(battery, bar.SelectedItem);
            Assert.IsFalse(bar.Select(0));
        }
    }
}
