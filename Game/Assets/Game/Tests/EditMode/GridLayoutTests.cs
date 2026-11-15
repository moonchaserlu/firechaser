using NUnit.Framework;
using UnityEngine;

namespace WildernessExtraction.Tests
{
    public sealed class GridLayoutTests
    {
        private static ItemDefinition Item(string id, int size, int maxStack = 1)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.size = size;
            item.maxStack = maxStack;
            item.category = size >= 2 ? ItemCategory.Weapon : ItemCategory.Material;
            return item;
        }

        [Test]
        public void Backpack_Is2x4_GunTakesTwoCells_AndCanRotate()
        {
            var bag = new Inventory(8);
            var gun = Item("gun", 2);
            var scrap = Item("scrap", 1);
            Assert.IsTrue(bag.TryAdd(gun));
            var layout = bag.Layout;
            Assert.AreEqual(2, layout.Width);
            Assert.AreEqual(4, layout.Height, "8 格背包是 2×4");
            var stack = bag.Stacks[0];
            var placement = layout[stack];
            Assert.AreEqual(2, placement.Width, "枪默认横放占两格");
            Assert.AreEqual(1, placement.Height);

            Assert.IsTrue(bag.MoveStack(stack, 1, 2, true), "竖放到右下角");
            Assert.AreEqual(1, layout[stack].Width);
            Assert.AreEqual(2, layout[stack].Height);
            Assert.IsFalse(bag.MoveStack(stack, 1, 3, true), "竖放超出底边放不下");

            Assert.IsTrue(bag.TryAdd(scrap));
            layout = bag.Layout;
            Assert.IsFalse(layout[bag.Stacks[1]].Covers(1, 2), "新东西不会压在枪上");
        }

        [Test]
        public void FullAreaAlwaysFits_ByRepacking()
        {
            var bag = new Inventory(8);
            var gun = Item("gun", 2);
            var scrap = Item("scrap", 1);
            // 先放 4 件 1 格的，故意摆散，再放 2 把枪：总面积 8，整理后一定放得下。
            for (var i = 0; i < 4; i++) Assert.IsTrue(bag.TryAdd(scrap));
            var layout = bag.Layout;
            bag.MoveStack(bag.Stacks[0], 0, 0, false);
            bag.MoveStack(bag.Stacks[1], 1, 1, false);
            bag.MoveStack(bag.Stacks[2], 0, 2, false);
            bag.MoveStack(bag.Stacks[3], 1, 3, false);
            Assert.IsTrue(bag.TryAdd(gun));
            Assert.IsTrue(bag.TryAdd(gun));
            layout = bag.Layout;
            Assert.AreEqual(6, layout.Keys.Count, "全部摆进网格");
            Assert.AreEqual(8, layout.UsedArea);
            Assert.IsFalse(bag.TryAdd(scrap), "满了");
        }

        [Test]
        public void Container_Is3x4_CorpseIs1x5()
        {
            var scrap = Item("scrap", 1);
            var gun = Item("gun", 2);
            var crate = new ContainerContents(new[] { scrap }, ContainerContents.GridWidth, ContainerContents.GridHeight);
            Assert.AreEqual(3, crate.Width);
            Assert.AreEqual(4, crate.Height);
            var corpse = new ContainerContents(new[] { gun }, ContainerContents.CorpseWidth, ContainerContents.CorpseHeight);
            Assert.AreEqual(1, corpse.Width);
            Assert.AreEqual(5, corpse.Height);
            Assert.IsTrue(corpse.PlacementAt(0).Rotated, "尸体只有一列，枪只能竖放");
            corpse.RevealAll();
            Assert.AreEqual(3, corpse.RoomFor(scrap), "1×5 去掉枪占的 2 格还剩 3 格");
            Assert.AreEqual(3, corpse.Store(scrap, 5), "放不下的不收");
        }
    }
}
