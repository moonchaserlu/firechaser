using NUnit.Framework;
using UnityEngine;

namespace WildernessExtraction.Tests
{
    /// <summary>参考鸭科夫的键鼠操作所依赖的纯逻辑：回血、中断换弹、快捷栏自动绑定、物品能否使用。</summary>
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
        public void QuickSlots_SetKeepsSelection_AutoAssignUsesFirstFreeItemSlot()
        {
            var pistol = items.Create("pistol", 1);
            var bandage = items.Create("bandage", 1);
            var spray = items.Create("spray", 1);
            var bar = new QuickSlotBar(8);

            Assert.IsTrue(bar.Set(0, pistol));
            Assert.AreEqual(-1, bar.SelectedIndex);
            Assert.AreEqual(2, bar.AutoAssign(bandage, PlayerItemUser.FirstItemSlot));
            Assert.AreEqual(-1, bar.AutoAssign(bandage, PlayerItemUser.FirstItemSlot), "已绑定的物品不重复绑定");
            bar.Assign(3, pistol);
            Assert.AreEqual(4, bar.AutoAssign(spray, PlayerItemUser.FirstItemSlot));
            Assert.IsTrue(bar.Contains(spray));
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
