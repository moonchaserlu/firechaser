using NUnit.Framework;

namespace WildernessExtraction.Tests
{
    public sealed class SettlementTests
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
        public void Extract_MovesInventoryIntoStash_AndClearsInventory()
        {
            var inventory = new Inventory(8);
            inventory.TryAdd(scrap);
            inventory.TryAdd(scrap);
            inventory.TryAdd(battery);
            var stash = new Stash();

            var result = RunSettlement.Extract(inventory, stash);

            Assert.AreEqual(RunOutcome.Extracted, result.Outcome);
            Assert.AreEqual(3, result.Items.Count);
            Assert.AreEqual(6, result.Value);
            Assert.AreEqual(0, inventory.Count);
            Assert.AreEqual(2, stash.CountOf(scrap));
            Assert.AreEqual(1, stash.CountOf(battery));
            Assert.AreEqual(3, stash.TotalItems);
            Assert.AreEqual(6, stash.TotalValue);
        }

        [Test]
        public void Die_ClearsInventory_StashUnchanged()
        {
            var stash = new Stash();
            stash.Deposit(new[] { canned });
            var inventory = new Inventory(8);
            inventory.TryAdd(battery);
            inventory.TryAdd(scrap);

            var result = RunSettlement.Die(inventory);

            Assert.AreEqual(RunOutcome.Died, result.Outcome);
            Assert.AreEqual(2, result.Items.Count);
            Assert.AreEqual(0, inventory.Count);
            Assert.AreEqual(1, stash.TotalItems);
            Assert.AreEqual(2, stash.TotalValue);
            Assert.AreEqual(0, stash.CountOf(battery));
        }

        [Test]
        public void Stash_AccumulatesAcrossRuns()
        {
            var stash = new Stash();
            var firstRun = new Inventory(8);
            firstRun.TryAdd(scrap);
            RunSettlement.Extract(firstRun, stash);

            var secondRun = new Inventory(8);
            secondRun.TryAdd(scrap);
            secondRun.TryAdd(canned);
            RunSettlement.Extract(secondRun, stash);

            Assert.AreEqual(2, stash.CountOf(scrap));
            Assert.AreEqual(1, stash.CountOf(canned));
            Assert.AreEqual(3, stash.TotalItems);
            Assert.AreEqual(4, stash.TotalValue);
            CollectionAssert.AreEqual(new[] { scrap, canned }, stash.ItemTypes);
        }

        [Test]
        public void Extract_EmptyInventory_IsValid()
        {
            var stash = new Stash();
            var result = RunSettlement.Extract(new Inventory(8), stash);
            Assert.AreEqual(0, result.Items.Count);
            Assert.AreEqual(0, stash.TotalItems);
        }
    }
}
