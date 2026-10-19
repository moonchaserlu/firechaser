#if false // Disabled: duplicates the focused EditMode test files in this directory.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace WildernessExtraction.Tests
{
    internal static class TestItems
    {
        public static ItemDefinition Create(string name, int value)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.name = name;
            item.displayName = name;
            item.stashValue = value;
            return item;
        }
    }

    public sealed class InventoryTests
    {
        private ItemDefinition scrap;

        [SetUp] public void SetUp() => scrap = TestItems.Create("废金属", 1);
        [TearDown] public void TearDown() => Object.DestroyImmediate(scrap);

        [Test]
        public void TryAdd_RejectsWhenFull()
        {
            var inventory = new Inventory(8);
            for (var i = 0; i < 8; i++)
            {
                Assert.IsTrue(inventory.TryAdd(scrap));
            }

            Assert.IsTrue(inventory.IsFull);
            Assert.IsFalse(inventory.TryAdd(scrap));
            Assert.AreEqual(8, inventory.Count);
        }

        [Test]
        public void TakeAll_ReturnsItemsAndClears()
        {
            var inventory = new Inventory(8);
            var changes = 0;
            inventory.Changed += () => changes++;
            inventory.TryAdd(scrap);
            inventory.TryAdd(scrap);

            var taken = inventory.TakeAll();

            Assert.AreEqual(2, taken.Count);
            Assert.AreEqual(0, inventory.Count);
            Assert.AreEqual(3, changes);
            inventory.TakeAll();
            Assert.AreEqual(3, changes, "清空空背包不应触发 Changed。");
        }

        [Test]
        public void TryAdd_NullThrows()
        {
            Assert.Throws<System.ArgumentNullException>(() => new Inventory(1).TryAdd(null));
        }
    }

    public sealed class StaminaModelTests
    {
        [Test]
        public void Consume_RejectsWhenInsufficient()
        {
            var stamina = new StaminaModel(100f, 20f, 0.5f);
            for (var i = 0; i < 4; i++)
            {
                Assert.IsTrue(stamina.TryConsume(25f));
            }

            Assert.IsFalse(stamina.TryConsume(25f));
            Assert.AreEqual(0f, stamina.Current, 1e-4f);
        }

        [Test]
        public void Regen_StartsAfterDelay()
        {
            var stamina = new StaminaModel(100f, 20f, 0.5f);
            stamina.TryConsume(25f);

            stamina.Tick(0.4f);
            Assert.AreEqual(75f, stamina.Current, 1e-4f);
            stamina.Tick(0.6f);
            Assert.AreEqual(85f, stamina.Current, 1e-3f, "前 0.1 秒仍在延迟内，之后 0.5 秒恢复 10 点。");
        }

        [Test]
        public void Regen_ClampsToMaxAndConsumeResetsDelay()
        {
            var stamina = new StaminaModel(100f, 20f, 0.5f);
            stamina.TryConsume(10f);
            stamina.Tick(10f);
            Assert.AreEqual(100f, stamina.Current, 1e-4f);

            stamina.TryConsume(10f);
            stamina.Tick(0.3f);
            stamina.TryConsume(10f);
            stamina.Tick(0.3f);
            Assert.AreEqual(80f, stamina.Current, 1e-4f);
        }
    }

    public sealed class WeaponStateTests
    {
        [Test]
        public void Fire_RespectsIntervalAndMagazine()
        {
            var weapon = new WeaponState(8, 0.25f, 1.4f);
            Assert.IsTrue(weapon.TryFire(0f));
            Assert.IsFalse(weapon.TryFire(0.1f));
            Assert.IsTrue(weapon.TryFire(0.25f));
            Assert.AreEqual(6, weapon.AmmoInMagazine);

            var now = 1f;
            while (weapon.TryFire(now))
            {
                now += 0.25f;
            }

            Assert.AreEqual(0, weapon.AmmoInMagazine);
            Assert.IsFalse(weapon.CanFire(now + 10f));
        }

        [Test]
        public void Reload_BlocksFiringThenRefills()
        {
            var weapon = new WeaponState(8, 0.25f, 1.4f);
            weapon.TryFire(0f);

            Assert.IsTrue(weapon.TryStartReload(1f));
            Assert.IsFalse(weapon.TryStartReload(1.1f));
            Assert.IsFalse(weapon.TryFire(2f));
            weapon.Tick(2.3f);
            Assert.IsTrue(weapon.IsReloading);
            weapon.Tick(2.5f);
            Assert.IsFalse(weapon.IsReloading);
            Assert.AreEqual(8, weapon.AmmoInMagazine);
        }

        [Test]
        public void Reload_RejectedWhenFull()
        {
            Assert.IsFalse(new WeaponState(8, 0.25f, 1.4f).TryStartReload(0f));
        }
    }

    public sealed class HealthTests
    {
        [Test]
        public void Model_ClampsAndIgnoresDamageAfterDeath()
        {
            var health = new HealthModel(75f);
            Assert.AreEqual(25f, health.ApplyDamage(25f));
            Assert.AreEqual(50f, health.ApplyDamage(80f));
            Assert.IsTrue(health.IsDead);
            Assert.AreEqual(0f, health.ApplyDamage(25f));
        }

        [Test]
        public void Component_DiesExactlyOnce()
        {
            var go = new GameObject("Health");
            try
            {
                var health = go.AddComponent<Health>();
                health.Configure(40f);
                var deaths = 0;
                health.Died += () => deaths++;

                Assert.IsTrue(health.TakeDamage(20f));
                Assert.IsTrue(health.TakeDamage(20f));
                Assert.IsFalse(health.TakeDamage(20f));
                Assert.AreEqual(1, deaths);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }

    public sealed class TimerTests
    {
        [Test]
        public void TimedProgress_CompletesOnceAndCancelResets()
        {
            var search = new TimedProgress();
            search.Start(1.5f);
            Assert.IsFalse(search.Tick(1f));
            search.Cancel();
            Assert.IsFalse(search.IsRunning);
            Assert.AreEqual(0f, search.Progress);

            search.Start(1.5f);
            Assert.IsFalse(search.Tick(1f));
            Assert.IsTrue(search.Tick(0.5f));
            Assert.IsFalse(search.Tick(1f));
        }

        [Test]
        public void Extraction_RequiresContinuousStay()
        {
            var timer = new ExtractionTimer(3f);
            Assert.AreEqual(ExtractionTick.Idle, timer.Tick(false, 1f));
            Assert.AreEqual(ExtractionTick.Started, timer.Tick(true, 2f));
            Assert.AreEqual(ExtractionTick.Interrupted, timer.Tick(false, 0.1f));
            Assert.AreEqual(0f, timer.Elapsed);

            Assert.AreEqual(ExtractionTick.Started, timer.Tick(true, 1f));
            Assert.AreEqual(ExtractionTick.Progressing, timer.Tick(true, 1f));
            Assert.AreEqual(ExtractionTick.Completed, timer.Tick(true, 1f));
            Assert.IsTrue(timer.IsComplete);
            Assert.AreEqual(ExtractionTick.Idle, timer.Tick(true, 1f));
        }
    }

    public sealed class LootAndSettlementTests
    {
        private ItemDefinition scrap;
        private ItemDefinition battery;
        private LootTable table;

        [SetUp]
        public void SetUp()
        {
            scrap = TestItems.Create("废金属", 1);
            battery = TestItems.Create("旧电池", 4);
            table = ScriptableObject.CreateInstance<LootTable>();
            table.entries = new List<LootTable.Entry>
            {
                new LootTable.Entry(scrap, 2),
                new LootTable.Entry(null, 3),
                new LootTable.Entry(battery, 1)
            };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(scrap);
            Object.DestroyImmediate(battery);
            Object.DestroyImmediate(table);
        }

        [Test]
        public void LootTable_ProducesFixedOrderedDrops()
        {
            CollectionAssert.AreEqual(new[] { scrap, scrap, battery }, table.CreateDrops());
            CollectionAssert.AreEqual(table.CreateDrops(), table.CreateDrops());
        }

        [Test]
        public void LootPile_FullInventoryKeepsRemainderWithoutDuplication()
        {
            var inventory = new Inventory(2);
            var pile = new LootPile(table.CreateDrops());

            var first = pile.TransferTo(inventory);
            Assert.AreEqual(2, first.Moved);
            Assert.AreEqual(1, first.Left);
            Assert.IsTrue(first.BlockedByFullInventory);
            Assert.AreEqual(3, inventory.Count + pile.Remaining.Count);

            inventory.TakeAll();
            var second = pile.TransferTo(inventory);
            Assert.AreEqual(1, second.Moved);
            Assert.IsTrue(pile.IsEmpty);
            Assert.AreEqual(battery, inventory.Items[0]);
        }

        [Test]
        public void Extract_DepositsIntoStashAndClearsInventory()
        {
            var inventory = new Inventory(8);
            var stash = new Stash();
            inventory.TryAdd(scrap);
            inventory.TryAdd(battery);

            var result = RunSettlement.Extract(inventory, stash);

            Assert.AreEqual(RunOutcome.Extracted, result.Outcome);
            Assert.AreEqual(5, result.Value);
            Assert.AreEqual(0, inventory.Count);
            Assert.AreEqual(1, stash.CountOf(scrap));
            Assert.AreEqual(1, stash.CountOf(battery));
            Assert.AreEqual(5, stash.TotalValue);

            inventory.TryAdd(scrap);
            RunSettlement.Extract(inventory, stash);
            Assert.AreEqual(2, stash.CountOf(scrap), "第二局撤离应在第一局基础上累加。");
        }

        [Test]
        public void Die_ClearsInventoryAndLeavesStashUnchanged()
        {
            var inventory = new Inventory(8);
            var stash = new Stash();
            stash.Deposit(new[] { scrap });
            inventory.TryAdd(battery);

            var result = RunSettlement.Die(inventory);

            Assert.AreEqual(RunOutcome.Died, result.Outcome);
            Assert.AreEqual(1, result.Items.Count);
            Assert.AreEqual(0, inventory.Count);
            Assert.AreEqual(1, stash.TotalItems);
            Assert.AreEqual(0, stash.CountOf(battery));
        }

        [Test]
        public void HudText_SummarizesItemsAndStash()
        {
            Assert.AreEqual("无", HudText.ItemSummary(new ItemDefinition[0]));
            Assert.AreEqual("废金属×2、旧电池×1", HudText.ItemSummary(new[] { scrap, battery, scrap }));

            var stash = new Stash();
            Assert.AreEqual("仓库：空", HudText.StashSummary(stash));
            stash.Deposit(new[] { battery, scrap });
            Assert.AreEqual("仓库（价值 5）：旧电池×1、废金属×1", HudText.StashSummary(stash));
        }
    }

    /// <summary>验收标准 8：配置默认值与 VerticalSlicePlan 第 4 节一致。</summary>
    public sealed class ConfigDefaultsTests
    {
        [Test]
        public void Defaults_MatchSlicePlan()
        {
            var player = ScriptableObject.CreateInstance<PlayerConfig>();
            var weapon = ScriptableObject.CreateInstance<WeaponConfig>();
            var enemy = ScriptableObject.CreateInstance<EnemyConfig>();
            var rules = ScriptableObject.CreateInstance<RunRulesConfig>();
            try
            {
                Assert.AreEqual(100f, player.maxHealth);
                Assert.AreEqual(5f, player.moveSpeed);
                Assert.AreEqual(0.35f, player.dodgeDuration);
                Assert.AreEqual(10f, player.dodgeSpeed);
                Assert.AreEqual(25f, player.dodgeStaminaCost);
                Assert.AreEqual(100f, player.maxStamina);
                Assert.AreEqual(20f, player.staminaRegenPerSecond);
                Assert.AreEqual(0.5f, player.staminaRegenDelay);
                Assert.AreEqual(8, weapon.magazineSize);
                Assert.AreEqual(25f, weapon.damage);
                Assert.AreEqual(0.25f, weapon.fireInterval);
                Assert.AreEqual(1.4f, weapon.reloadDuration);
                Assert.AreEqual(75f, enemy.maxHealth);
                Assert.AreEqual(20f, enemy.damage);
                Assert.AreEqual(1f, enemy.attackInterval);
                Assert.AreEqual(8f, enemy.alertRadius);
                Assert.AreEqual(16, rules.inventoryCapacity);
                Assert.AreEqual(1.5f, rules.searchDuration);
                Assert.AreEqual(3f, rules.extractionDuration);
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(weapon);
                Object.DestroyImmediate(enemy);
                Object.DestroyImmediate(rules);
            }
        }
    }
}
#endif
