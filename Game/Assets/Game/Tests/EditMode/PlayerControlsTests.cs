using NUnit.Framework;
using UnityEngine;

namespace WildernessExtraction.Tests
{
    /// <summary>参考鸭科夫的键鼠操作所依赖的纯逻辑：回血、中断换弹、快捷栏手动装卸、物品能否使用。</summary>
    public sealed class PlayerControlsTests
    {
        private TestItems items;

        [SetUp]
        public void SetUp()
        {
            items = new TestItems();
        }

        [TearDown]
        public void TearDown()
        {
            items.DestroyAll();
        }

        [Test]
        public void Heal_ClampsAtMax_AndIgnoredWhenDead()
        {
            var health = new HealthModel(100f);
            health.ApplyDamage(30f);
            Assert.AreEqual(20f, health.Heal(20f));
            Assert.AreEqual(10f, health.Heal(40f));
            Assert.AreEqual(100f, health.Current);
            Assert.AreEqual(0f, health.Heal(-5f));

            health.ApplyDamage(100f);
            Assert.AreEqual(0f, health.Heal(50f));
            Assert.IsTrue(health.IsDead);
        }

        [Test]
        public void CancelReload_KeepsMagazine_AndAllowsNewReload()
        {
            var weapon = new WeaponState(8, 0.1f, 1f);
            for (var i = 0; i < 5; i++) Assert.IsTrue(weapon.TryFire(i));
            Assert.IsTrue(weapon.TryStartReload(10f, 30));
            weapon.CancelReload();
            weapon.Tick(20f);
            Assert.IsFalse(weapon.IsReloading);
            Assert.AreEqual(3, weapon.AmmoInMagazine);
            Assert.IsTrue(weapon.TryStartReload(20f, 30));
            weapon.Tick(21f);
            Assert.AreEqual(8, weapon.AmmoInMagazine);
        }

        [Test]
        public void QuickSlots_DoNotFillThemselves_PlayerDragsItemsOn()
        {
            var pistol = items.Create("pistol", 2);
            pistol.category = ItemCategory.Weapon;
            var bandage = Usable(items.Create("bandage", 1));
            var scrap = items.Create("scrap", 1);
            var bar = new QuickSlotBar(8);
            for (var i = 0; i < bar.Capacity; i++) Assert.IsNull(bar[i], "新快捷栏是空的");

            // 能用的东西只进 3–8；拖到 1、2 号格被拒绝。
            Assert.AreEqual(QuickSlotBindResult.WeaponSlotsOnly, QuickSlotRules.Bind(bar, 0, bandage, 3, -1, out _));
            Assert.AreEqual(QuickSlotBindResult.Bound, QuickSlotRules.Bind(bar, 4, bandage, 3, -1, out var bound));
            Assert.AreEqual(4, bound);
            Assert.AreEqual(-1, bar.SelectedIndex, "装上不改变选中");

            // 同一件东西只占一格：拖到新格子时从旧格子挪过去。
            QuickSlotRules.Bind(bar, 2, bandage, 3, -1, out _);
            Assert.AreSame(bandage, bar[2]);
            Assert.IsNull(bar[4]);

            // 武器不管拖到哪一格都进它自己的武器槽。
            Assert.AreEqual(QuickSlotBindResult.Bound, QuickSlotRules.Bind(bar, 6, pistol, 1, 0, out bound));
            Assert.AreEqual(0, bound);
            Assert.AreSame(pistol, bar[0]);
            Assert.IsNull(bar[6]);

            // 不是角色武器的枪、材料、背包里没有的东西都装不上。
            var rifle = items.Create("rifle", 2);
            rifle.category = ItemCategory.Weapon;
            Assert.AreEqual(QuickSlotBindResult.WeaponUnsupported, QuickSlotRules.Bind(bar, 1, rifle, 1, -1, out _));
            Assert.AreEqual(QuickSlotBindResult.NotUsable, QuickSlotRules.Bind(bar, 5, scrap, 4, -1, out _));
            Assert.AreEqual(QuickSlotBindResult.NotInBag, QuickSlotRules.Bind(bar, 5, Usable(items.Create("spray", 1)), 0, -1, out _));
        }

        [Test]
        public void QuickSlots_MoveSwapsItemSlots_ButNotWeaponSlots()
        {
            var pistol = items.Create("pistol", 2);
            var bandage = Usable(items.Create("bandage", 1));
            var food = Usable(items.Create("food", 1));
            var bar = new QuickSlotBar(8);
            bar.Set(0, pistol);
            bar.Set(2, bandage);
            bar.Set(5, food);

            Assert.IsTrue(QuickSlotRules.Move(bar, 2, 5));
            Assert.AreSame(food, bar[2]);
            Assert.AreSame(bandage, bar[5]);
            Assert.IsTrue(QuickSlotRules.Move(bar, 5, 7));
            Assert.AreSame(bandage, bar[7]);
            Assert.IsNull(bar[5]);
            Assert.IsFalse(QuickSlotRules.Move(bar, 0, 3), "武器格不能拖到物品格");
            Assert.IsFalse(QuickSlotRules.Move(bar, 3, 1), "物品格不能拖到武器格");
            bar.Clear(7);
            Assert.IsFalse(bar.Contains(bandage));
        }

        [Test]
        public void QuickSlotMemory_KeepsOnlyWhatThePlayerEquipped()
        {
            QuickSlotMemory.Reset();
            var bandage = Usable(items.Create("bandage", 1));
            var first = new QuickSlotBar(8);
            first.Set(3, bandage);
            QuickSlotMemory.Save(first);

            var next = new QuickSlotBar(8);
            QuickSlotMemory.Restore(next);
            Assert.AreSame(bandage, next[3], "换场景后保留玩家装好的快捷栏");

            QuickSlotMemory.Reset();
            var fresh = new QuickSlotBar(8);
            QuickSlotMemory.Restore(fresh);
            Assert.IsNull(fresh[3], "清空后新快捷栏是空的");
        }

        private static ItemDefinition Usable(ItemDefinition item)
        {
            item.useDuration = 1f;
            item.stopsBleeding = true;
            return item;
        }

        [Test]
        public void ItemUseRules_RequireEffectAndStock()
        {
            var bandage = items.Create("bandage", 1);
            bandage.useDuration = 2f;
            bandage.stopsBleeding = true;
            var food = items.Create("food", 1);
            food.useDuration = 2.5f;
            food.healAmount = 15f;
            var scrap = items.Create("scrap", 1);

            Assert.IsTrue(bandage.IsUsable);
            Assert.IsFalse(scrap.IsUsable);
            Assert.AreEqual(ItemUseCheck.NotUsable, ItemUseRules.Check(scrap, 3, 50f, 100f, true));
            Assert.AreEqual(ItemUseCheck.NoneLeft, ItemUseRules.Check(bandage, 0, 50f, 100f, true));
            Assert.AreEqual(ItemUseCheck.Ok, ItemUseRules.Check(bandage, 1, 100f, 100f, true));
            Assert.AreEqual(ItemUseCheck.NotNeeded, ItemUseRules.Check(bandage, 1, 50f, 100f, false));
            Assert.AreEqual(ItemUseCheck.Ok, ItemUseRules.Check(food, 1, 50f, 100f, false));
            Assert.AreEqual(ItemUseCheck.NotNeeded, ItemUseRules.Check(food, 1, 100f, 100f, true));
        }
    }
}
