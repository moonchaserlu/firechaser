using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace WildernessExtraction.Shelter.Tests
{
    /// <summary>局外存档与出发交接的纯规则。</summary>
    public sealed class PlayerProfileTests
    {
        private string path;
        private ItemDatabase database;
        private ItemDefinition scrap;
        private ItemDefinition pistol;

        [SetUp]
        public void SetUp()
        {
            path = Path.Combine(Path.GetTempPath(), $"firechaser_profile_test_{System.Guid.NewGuid():N}.json");
            PlayerProfile.PathOverride = path;
            scrap = Item("scrap_metal", 5, ItemCategory.Material, 5);
            pistol = Item("old_pistol", 45, ItemCategory.Weapon, 1);
            database = ScriptableObject.CreateInstance<ItemDatabase>();
            database.items.Add(scrap);
            database.items.Add(pistol);
            RaidTransfer.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerProfile.PathOverride = null;
            if (File.Exists(path)) File.Delete(path);
            RaidTransfer.Reset();
        }

        [Test]
        public void MissingSave_GivesEmptyProfile()
        {
            var profile = PlayerProfile.LoadOrCreate(database);
            Assert.AreEqual(0, profile.Credits);
            Assert.AreEqual(0, profile.Stash.ItemCount);
            Assert.AreEqual(PlayerProfile.DefaultStashCapacity, profile.Stash.Capacity);
        }

        [Test]
        public void SaveAndLoad_RoundTripsStashAndRaids()
        {
            var profile = PlayerProfile.CreateNew();
            for (var i = 0; i < 7; i++) profile.Stash.TryAdd(scrap);
            profile.Stash.TryAdd(pistol);
            profile.AddCredits(120);
            profile.RecordRaid();
            profile.ExpandStash(8);
            profile.Save();

            var loaded = PlayerProfile.LoadOrCreate(database);
            Assert.AreEqual(7, loaded.Stash.CountOf(scrap));
            Assert.AreEqual(1, loaded.Stash.CountOf(pistol));
            Assert.AreEqual(120, loaded.Credits);
            Assert.AreEqual(1, loaded.CompletedRaids);
            Assert.AreEqual(PlayerProfile.DefaultStashCapacity + 8, loaded.Stash.Capacity);
        }

        [Test]
        public void UnknownItems_AreSkippedNotCrashing()
        {
            var profile = PlayerProfile.CreateNew();
            profile.Stash.TryAdd(scrap);
            profile.Stash.TryAdd(pistol);
            profile.Save();

            database.items.Remove(pistol);
            var fresh = ScriptableObject.CreateInstance<ItemDatabase>();
            fresh.items.Add(scrap);
            var loaded = PlayerProfile.LoadOrCreate(fresh);
            Assert.AreEqual(1, loaded.Stash.CountOf(scrap));
            Assert.AreEqual(1, loaded.UnknownItems);
        }

        [Test]
        public void CorruptSave_FallsBackToNewProfile()
        {
            File.WriteAllText(path, "{ not json");
            var loaded = PlayerProfile.LoadOrCreate(database);
            Assert.AreEqual(0, loaded.Stash.ItemCount);
        }

        [Test]
        public void RaidTransfer_ReturnBringsItems_DeathBringsNothing()
        {
            RaidTransfer.Begin(new[] { pistol, scrap });
            Assert.IsTrue(RaidTransfer.Active);
            Assert.AreEqual(2, RaidTransfer.Loadout.Count);

            RaidTransfer.Finish(RaidOutcome.Returned, new[] { pistol, scrap, scrap });
            Assert.IsFalse(RaidTransfer.Active);
            var (outcome, items) = RaidTransfer.Collect();
            Assert.AreEqual(RaidOutcome.Returned, outcome);
            Assert.AreEqual(3, items.Count);
            Assert.IsFalse(RaidTransfer.HasPendingReturn, "结果只能领取一次");

            RaidTransfer.Begin(new[] { pistol });
            RaidTransfer.Finish(RaidOutcome.Died, new[] { pistol });
            var (deathOutcome, lost) = RaidTransfer.Collect();
            Assert.AreEqual(RaidOutcome.Died, deathOutcome);
            Assert.AreEqual(0, lost.Count, "死亡不带回任何东西");
        }

        private static ItemDefinition Item(string id, int value, ItemCategory category, int stack)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = id;
            item.stashValue = value;
            item.category = category;
            item.maxStack = stack;
            return item;
        }
    }
}
