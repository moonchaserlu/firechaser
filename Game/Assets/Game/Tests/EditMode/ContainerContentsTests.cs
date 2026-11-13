using NUnit.Framework;

namespace WildernessExtraction.Tests
{
    public sealed class ContainerContentsTests
    {
        private TestItems items;
        private ItemDefinition scrap;
        private ItemDefinition cloth;
        private ItemDefinition relic;

        [SetUp]
        public void SetUp()
        {
            items = new TestItems();
            scrap = items.Create("scrap_metal", 1);
            cloth = items.Create("cloth", 1);
            relic = items.Create("relic", 10);
            relic.rarity = ItemRarity.Rare;
        }

        [TearDown]
        public void TearDown()
        {
            items.DestroyAll();
        }

        [Test]
        public void RevealsOneItemAtATime_InOrder()
        {
            var contents = new ContainerContents(new[] { scrap, null, cloth });
            Assert.AreEqual(2, contents.Count, "空引用被丢掉");
            Assert.IsFalse(contents.AllRevealed);
            Assert.AreEqual(0, contents.NextHidden);

            Assert.AreEqual(0, contents.RevealNext());
            Assert.IsTrue(contents.IsRevealed(0));
            Assert.IsFalse(contents.IsRevealed(1));
            Assert.AreEqual(1, contents.RevealNext());
            Assert.IsTrue(contents.AllRevealed);
            Assert.AreEqual(-1, contents.RevealNext());
        }

        [Test]
        public void HiddenItemsCannotBeTaken()
        {
            var contents = new ContainerContents(new[] { scrap, cloth });
            var bag = new Inventory(8);
            Assert.IsFalse(contents.TakeAt(0, bag), "还没搜出的物品拿不走");
            contents.RevealNext();
            var result = contents.TakeAllRevealed(bag);
            Assert.AreEqual(1, result.Moved, "只拿走搜出来的那件");
            Assert.AreEqual(1, contents.Count);
            Assert.AreSame(cloth, contents[0]);
            Assert.IsFalse(contents.IsRevealed(0), "剩下的那件仍然没搜");
        }

        [Test]
        public void TakeAt_MovesSingleItem_AndKeepsTheRest()
        {
            var contents = new ContainerContents(new[] { scrap, cloth, relic });
            contents.RevealAll();
            var bag = new Inventory(8);

            Assert.IsTrue(contents.TakeAt(1, bag));
            Assert.AreEqual(1, bag.CountOf(cloth));
            Assert.AreEqual(2, contents.Count);
            Assert.AreSame(scrap, contents[0]);
            Assert.AreSame(relic, contents[1]);
        }

        [Test]
        public void FullBackpack_LeavesItemInContainer()
        {
            var contents = new ContainerContents(new[] { scrap, cloth });
            contents.RevealAll();
            var bag = new Inventory(1);
            Assert.IsTrue(contents.TakeAt(0, bag));
            Assert.IsFalse(contents.TakeAt(0, bag), "背包满了放不下");
            Assert.AreEqual(1, contents.Count, "放不下的物品留在容器里");

            var result = contents.TakeAllRevealed(bag);
            Assert.AreEqual(0, result.Moved);
            Assert.AreEqual(1, result.Left);
            Assert.IsTrue(result.BlockedByFullInventory);
        }

        [Test]
        public void RarerItemsTakeLongerToSearch()
        {
            var common = ContainerContents.SearchSeconds(scrap, 1.2f);
            var rare = ContainerContents.SearchSeconds(relic, 1.2f);
            Assert.AreEqual(0.6f, common, 1e-4f);
            Assert.Greater(rare, common);
            Assert.GreaterOrEqual(ContainerContents.SearchSeconds(scrap, 0f), ContainerContents.MinItemSeconds, "瞬开的容器每件也有最短时间");
        }
    }
}
