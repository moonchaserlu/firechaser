using System;
using System.IO;
using NUnit.Framework;

namespace WildernessExtraction.Tests
{
    public sealed class SaveSlotsTests
    {
        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "firechaser_slots_" + Guid.NewGuid().ToString("N"));
            SaveSlots.DirectoryOverride = directory;
            GameSettings.PathOverride = Path.Combine(directory, "settings.json");
            SaveSlots.ClearActive();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSlots.ClearActive();
            SaveSlots.DirectoryOverride = null;
            GameSettings.PathOverride = null;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void EmptyDirectoryHasNoSlots()
        {
            Assert.That(SaveSlots.FirstEmpty(), Is.EqualTo(1));
            Assert.That(SaveSlots.MostRecent(), Is.EqualTo(0));
            Assert.That(SaveSlots.LoadAll(), Has.All.Null);
        }

        [Test]
        public void CreateNewFillsSlotAndMakesItActive()
        {
            var now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
            SaveSlots.CreateNew(1, GameScenes.Map01, now);

            var info = SaveSlots.Load(1);
            Assert.That(info, Is.Not.Null);
            Assert.That(info.scenePath, Is.EqualTo(GameScenes.Map01));
            Assert.That(info.sessions, Is.EqualTo(1));
            Assert.That(info.LastPlayed, Is.EqualTo(now));
            Assert.That(SaveSlots.ActiveSlot, Is.EqualTo(1));
            Assert.That(SaveSlots.FirstEmpty(), Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => SaveSlots.CreateNew(1, GameScenes.Map01, now));
        }

        [Test]
        public void MostRecentFollowsLastPlayed()
        {
            var t = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
            SaveSlots.CreateNew(1, GameScenes.Map01, t);
            SaveSlots.CreateNew(2, GameScenes.Map01, t.AddHours(1));
            Assert.That(SaveSlots.MostRecent(), Is.EqualTo(2));

            SaveSlots.Resume(1, t.AddHours(2));
            Assert.That(SaveSlots.MostRecent(), Is.EqualTo(1));
            Assert.That(SaveSlots.Load(1).sessions, Is.EqualTo(2));
        }

        [Test]
        public void PlayTimeAccumulatesOnActiveSlotOnly()
        {
            var t = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
            SaveSlots.AddPlayTime(30, t);
            Assert.That(SaveSlots.LoadAll(), Has.All.Null);

            SaveSlots.CreateNew(3, GameScenes.Map01, t);
            SaveSlots.AddPlayTime(30, t);
            SaveSlots.AddPlayTime(45.5, t);
            Assert.That(SaveSlots.Load(3).playSeconds, Is.EqualTo(75.5).Within(1e-6));
            Assert.That(SaveSlots.FormatPlayTime(3725), Is.EqualTo("01:02:05"));
        }

        [Test]
        public void ProfileUsesSlotFileWhileSlotIsActive()
        {
            var legacy = PlayerProfile.SavePath;
            SaveSlots.CreateNew(2, GameScenes.Map01, DateTime.UtcNow);
            Assert.That(PlayerProfile.SavePath, Is.EqualTo(SaveSlots.ProfilePath(2)));

            var profile = PlayerProfile.CreateNew();
            profile.AddCredits(42);
            profile.Save();
            Assert.That(File.Exists(SaveSlots.ProfilePath(2)), Is.True);

            SaveSlots.Delete(2);
            Assert.That(File.Exists(SaveSlots.ProfilePath(2)), Is.False);
            Assert.That(SaveSlots.Exists(2), Is.False);
            Assert.That(SaveSlots.ActiveSlot, Is.EqualTo(0));
            Assert.That(PlayerProfile.SavePath, Is.EqualTo(legacy));
        }

        [Test]
        public void CorruptSlotIsTreatedAsEmpty()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(SaveSlots.InfoPath(1), "{ not json");
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Assert.That(SaveSlots.Load(1), Is.Null);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void SettingsRoundTripAndClamp()
        {
            var settings = new GameSettings { displayMode = 2, width = 1280, height = 720, vSync = false, brightness = 140, masterVolume = 35 };
            settings.Save();

            var loaded = GameSettings.Load();
            Assert.That(loaded.displayMode, Is.EqualTo(2));
            Assert.That(loaded.width, Is.EqualTo(1280));
            Assert.That(loaded.vSync, Is.False);
            Assert.That(loaded.brightness, Is.EqualTo(100));
            Assert.That(loaded.masterVolume, Is.EqualTo(35));
            Assert.That(loaded.FullScreen, Is.EqualTo(UnityEngine.FullScreenMode.Windowed));
        }
    }
}
