using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WildernessExtraction.Tests
{
    public sealed class MainMenuTests
    {
        private string directory;
        private string loadedScene;
        private Action<string> previousOverride;
        private GameObject menuObject;
        private MainMenuController menu;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "firechaser_menu_" + Guid.NewGuid().ToString("N"));
            SaveSlots.DirectoryOverride = directory;
            GameSettings.PathOverride = Path.Combine(directory, "settings.json");
            GameSettings.Current = null;
            previousOverride = GameScenes.LoadOverride;
            loadedScene = null;
            GameScenes.LoadOverride = path => loadedScene = path;
        }

        [TearDown]
        public void TearDown()
        {
            if (menuObject != null) UnityEngine.Object.Destroy(menuObject);
            GameScenes.LoadOverride = previousOverride;
            SaveSlots.ClearActive();
            SaveSlots.DirectoryOverride = null;
            GameSettings.PathOverride = null;
            GameSettings.Current = null;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private IEnumerator CreateMenu()
        {
            menuObject = new GameObject("MainMenu");
            menu = menuObject.AddComponent<MainMenuController>();
            yield return null;
        }

        private IEnumerator WaitForLoad()
        {
            var deadline = Time.unscaledTime + 5f;
            while (loadedScene == null && Time.unscaledTime < deadline) yield return null;
        }

        [UnityTest]
        public IEnumerator StartGameCreatesSlotAndLoadsGameScene()
        {
            yield return CreateMenu();
            Assert.That(menu.CanContinue, Is.False);

            menu.RequestStart();
            Assert.That(menu.Dialog, Is.EqualTo(MainMenuDialog.StartNew));
            menu.ConfirmStart();
            Assert.That(menu.Page, Is.EqualTo(MainMenuPage.Loading));

            yield return WaitForLoad();
            Assert.That(loadedScene, Is.EqualTo(GameScenes.Tutorial), "新存档先进开局教学关");
            Assert.That(SaveSlots.Load(1).scenePath, Is.EqualTo(GameScenes.Tutorial));
            Assert.That(SaveSlots.Exists(1), Is.True);
            Assert.That(SaveSlots.ActiveSlot, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ContinueResumesMostRecentSlot()
        {
            SaveSlots.CreateNew(1, GameScenes.Map01, DateTime.UtcNow.AddHours(-2));
            SaveSlots.CreateNew(3, GameScenes.Map01, DateTime.UtcNow.AddHours(-1));
            yield return CreateMenu();

            Assert.That(SaveSlots.ActiveSlot, Is.EqualTo(0), "进入主菜单时清掉当前槽位");
            Assert.That(menu.CanContinue, Is.True);
            menu.Continue();
            yield return WaitForLoad();

            Assert.That(loadedScene, Is.EqualTo(GameScenes.Map01));
            Assert.That(SaveSlots.ActiveSlot, Is.EqualTo(3));
            Assert.That(SaveSlots.Load(3).sessions, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator FullSlotsAskToDeleteFirst()
        {
            for (var slot = 1; slot <= SaveSlots.SlotCount; slot++) SaveSlots.CreateNew(slot, GameScenes.Map01, DateTime.UtcNow);
            yield return CreateMenu();

            menu.RequestStart();
            Assert.That(menu.Dialog, Is.EqualTo(MainMenuDialog.SlotsFull));

            menu.OpenSaves();
            Assert.That(menu.Page, Is.EqualTo(MainMenuPage.Saves));
            menu.RequestDelete(2);
            Assert.That(menu.Dialog, Is.EqualTo(MainMenuDialog.DeleteSlot));
            menu.ConfirmDelete();
            Assert.That(SaveSlots.Exists(2), Is.False);
            Assert.That(menu.Slots[1], Is.Null);

            menu.Back();
            Assert.That(menu.Page, Is.EqualTo(MainMenuPage.Main));
        }

        [UnityTest]
        public IEnumerator SettingsApplySavesAndBackDiscards()
        {
            yield return CreateMenu();

            menu.OpenSettings();
            menu.SelectTab(1);
            menu.AdjustRow(1, 0, -1);
            Assert.That(menu.EditingSettings.masterVolume, Is.EqualTo(75));
            menu.Back();
            Assert.That(GameSettings.Current.masterVolume, Is.EqualTo(80), "返回不保存");

            menu.OpenSettings();
            menu.AdjustRow(1, 0, -1);
            menu.AdjustRow(0, 3, 1);
            menu.ApplySettings();
            Assert.That(menu.Page, Is.EqualTo(MainMenuPage.Main));
            Assert.That(GameSettings.Load().masterVolume, Is.EqualTo(75));
            Assert.That(GameSettings.Load().vSync, Is.False);
            Assert.That(AudioListener.volume, Is.EqualTo(0.75f).Within(1e-4));
            AudioListener.volume = 1f;
        }

        [UnityTest]
        public IEnumerator QuitAsksForConfirmation()
        {
            yield return CreateMenu();
            menu.RequestQuit();
            Assert.That(menu.Dialog, Is.EqualTo(MainMenuDialog.Quit));
            menu.Back();
            Assert.That(menu.Dialog, Is.EqualTo(MainMenuDialog.None));
            Assert.That(menu.Page, Is.EqualTo(MainMenuPage.Main));
        }
    }
}
