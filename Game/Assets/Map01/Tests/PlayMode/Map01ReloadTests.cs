using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>
    /// 换弹在 Map01 玩家身上真正生效：用测试键盘/鼠标按 R、扣扳机，
    /// 验证换弹耗时、从背包扣子弹、没备弹时提示，以及短刀挥砍不会触发换弹。
    /// </summary>
    public sealed class Map01ReloadTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";

        private PlayerCharacter player;
        private Keyboard keyboard;
        private Mouse mouse;
        private InputSettings originalSettings;
        private InputSettings testSettings;

        [UnitySetUp]
        public IEnumerator Load()
        {
            Map01Authority.SuppressMonsters = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("只在编辑器中运行。");
#endif
            yield return null;
            player = Object.FindAnyObjectByType<Map01Authority>().Player;
            var interactor = player.GetComponent<Map01Interactor>();
            if (interactor != null) interactor.enabled = false;
            player.Input.HotkeysSuppressed = false;
            // 同 Map01PlayerControlsTests：换成忽略焦点的输入设置副本，保证测试设备的输入能送达。
            var current = InputSystem.settings;
            originalSettings = current;
            if (current.hideFlags == HideFlags.HideAndDontSave)
            {
                originalSettings = Object.Instantiate(current);
                originalSettings.hideFlags = HideFlags.HideAndDontSave;
            }

            testSettings = Object.Instantiate(current);
            testSettings.hideFlags = HideFlags.HideAndDontSave;
            testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = testSettings;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
        }

        [TearDown]
        public void Restore()
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            keyboard = null;
            mouse = null;
            if (originalSettings != null) InputSystem.settings = originalSettings;
            if (testSettings != null) Object.Destroy(testSettings);
            originalSettings = null;
            testSettings = null;
            Map01Authority.SuppressMonsters = false;
        }

        [UnityTest]
        public IEnumerator KeyR_ReloadsAfterDuration_ConsumesInventoryAmmo()
        {
            var shooter = player.Shooter;
            var config = shooter.CurrentConfig;
            Assert.AreNotEqual(WeaponKind.Melee, config.kind, "Map01 主武器应是枪");
            Assert.IsNotNull(config.ammoItem);
            Assert.IsNotNull(player.GetComponent<PlayerAmmoHud>(), "玩家身上自动挂弹药 HUD");
            SetReserve(config.ammoItem, 20);
            EmptyMagazine(shooter.Weapon, 3);
            var reloads = 0;
            shooter.ReloadStarted += () => reloads++;

            yield return Tap(Key.R);
            Assert.AreEqual(1, reloads, "按 R 开始换弹");
            Assert.IsTrue(shooter.Weapon.IsReloading);
            Assert.AreEqual(3, shooter.Weapon.AmmoInMagazine, "换弹完成前弹匣不变");
            Assert.AreEqual(20, Reserve(config.ammoItem), "换弹完成前不扣背包子弹");

            yield return new WaitForSeconds(config.reloadDuration + 0.2f);
            Assert.IsFalse(shooter.Weapon.IsReloading);
            Assert.AreEqual(shooter.Weapon.MagazineSize, shooter.Weapon.AmmoInMagazine, "换满弹匣");
            Assert.AreEqual(20 - (shooter.Weapon.MagazineSize - 3), Reserve(config.ammoItem), "只扣装进去的子弹");

            yield return Tap(Key.R);
            Assert.AreEqual(1, reloads, "弹匣满时按 R 不换弹");
        }

        [UnityTest]
        public IEnumerator ReloadWithFewRounds_LoadsOnlyWhatIsCarried()
        {
            var shooter = player.Shooter;
            var ammo = shooter.CurrentConfig.ammoItem;
            SetReserve(ammo, 2);
            EmptyMagazine(shooter.Weapon, 0);
            yield return Tap(Key.R);
            yield return new WaitForSeconds(shooter.CurrentConfig.reloadDuration + 0.2f);
            Assert.AreEqual(2, shooter.Weapon.AmmoInMagazine, "只装得进背包里剩下的 2 发");
            Assert.AreEqual(0, Reserve(ammo));
        }

        [UnityTest]
        public IEnumerator NoReserveAmmo_KeyROrEmptyTrigger_NotifiesOutOfAmmo()
        {
            var shooter = player.Shooter;
            SetReserve(shooter.CurrentConfig.ammoItem, 0);
            EmptyMagazine(shooter.Weapon, 0);
            var outOfAmmo = 0;
            var reloads = 0;
            shooter.OutOfAmmo += () => outOfAmmo++;
            shooter.ReloadStarted += () => reloads++;

            yield return Tap(Key.R);
            Assert.AreEqual(1, outOfAmmo, "没备弹按 R 提示");
            Assert.IsTrue(player.GetComponent<PlayerAmmoHud>().OutOfAmmoNoticeVisible);

            // 空弹匣按住左键：只在按下那一帧提示一次，不会每帧刷屏。
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
            for (var i = 0; i < 10; i++) yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;
            Assert.AreEqual(2, outOfAmmo, "空弹匣扣扳机提示一次");
            Assert.AreEqual(0, reloads, "没有子弹不会开始换弹");
            Assert.IsFalse(shooter.Weapon.IsReloading);
        }

        [UnityTest]
        public IEnumerator KnifeSwings_NeverTriggerReload()
        {
            var shooter = player.Shooter;
            var knife = shooter.SlotItem(1);
            Assert.IsNotNull(knife, "Player 预制体应配置副武器（短刀）");
            player.Inventory.Inventory.TryAdd(knife);
            yield return Tap(Key.Digit2);
            Assert.AreEqual(WeaponKind.Melee, shooter.CurrentConfig.kind);
            yield return new WaitForSeconds(shooter.CurrentConfig.equipDuration + 0.05f);

            var reloads = 0;
            var swings = 0;
            shooter.ReloadStarted += () => reloads++;
            shooter.Fired += () => swings++;
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
            yield return new WaitForSeconds(shooter.CurrentConfig.fireInterval * 2.5f);
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;

            Assert.GreaterOrEqual(swings, 2, "按住左键连续挥刀");
            Assert.AreEqual(0, reloads, "短刀挥砍不会触发换弹（和换弹动画）");
            Assert.IsFalse(shooter.Weapon.IsReloading);
        }

        private void SetReserve(ItemDefinition ammo, int count)
        {
            var bag = player.Inventory.Inventory;
            bag.Remove(ammo, bag.CountOf(ammo));
            for (var i = 0; i < count; i++) Assert.IsTrue(bag.TryAdd(ammo), "背包装得下测试子弹");
            Assert.AreEqual(count, bag.CountOf(ammo));
        }

        private int Reserve(ItemDefinition ammo) => player.Inventory.Inventory.CountOf(ammo);

        // 直接打掉弹匣里的子弹（不经过输入），剩 left 发。每发传一个更晚的时间绕过射击间隔；之后这些测试不再开枪，所以不影响。
        private static void EmptyMagazine(WeaponState weapon, int left)
        {
            var guard = 0;
            while (weapon.AmmoInMagazine > left && guard++ < 1000)
            {
                weapon.TryFire(Time.time + guard * 10f);
            }

            Assert.AreEqual(left, weapon.AmmoInMagazine);
        }

        private IEnumerator Tap(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }
    }
}
