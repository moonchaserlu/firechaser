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
    /// 键鼠操作（参考鸭科夫）在 Map01 玩家身上真正生效：组件齐全、动作来自输入资产，
    /// 并用一块测试键盘按下 Shift / 2 / C / 3 验证奔跑、切武器、收枪和用绷带。
    /// </summary>
    public sealed class Map01PlayerControlsTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";
        private const string BandagePath = "Assets/Game/Data/Items/Item_DirtyBandage.asset";

        private PlayerCharacter player;
        private Keyboard keyboard;
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
            // 出生点附近可能有商店终端，商店会占用数字键；测试里关掉设施交互。
            var interactor = player.GetComponent<Map01Interactor>();
            if (interactor != null) interactor.enabled = false;
            player.Input.HotkeysSuppressed = false;
            // 批处理/编辑器无焦点时 InputSystem 默认会丢弃键盘事件；测试期间换成忽略焦点的设置副本，保证测试键盘的按键能送达。
            // InputSystem 换设置时会 DestroyImmediate 它自建的临时设置（HideAndDontSave），所以临时设置先复制一份留作还原。
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
        }

        [TearDown]
        public void Restore()
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            keyboard = null;
            if (originalSettings != null) InputSystem.settings = originalSettings;
            if (testSettings != null) Object.Destroy(testSettings);
            originalSettings = null;
            testSettings = null;
            Map01Authority.SuppressMonsters = false;
        }

        [UnityTest]
        public IEnumerator Player_HasControlComponents_AndAllActionsFromAsset()
        {
            yield return null;
            Assert.IsNotNull(player.GetComponent<PlayerItemUser>(), "快捷栏用物品组件");
            Assert.IsNotNull(player.GetComponent<PlayerStatusHud>(), "玩家 HUD（快捷栏、读条、准星）");
            Assert.AreEqual(0, player.Input.FallbackActionCount, "所有动作都应来自 InputSystem_Actions，没有回退到代码默认绑定");
            Assert.IsTrue(player.Input.GameplayEnabled);
        }

        [UnityTest]
        public IEnumerator HoldShiftAndW_Sprints()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(player.Motor.IsSprinting, "按住 Shift + W 应奔跑");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;
            Assert.IsFalse(player.Motor.IsSprinting);
        }

        [UnityTest]
        public IEnumerator Key2_SwitchesToSecondary_KeyC_Holsters()
        {
            var secondary = player.Shooter.SlotItem(1);
            Assert.IsNotNull(secondary, "Player 预制体应配置副武器");
            player.Inventory.Inventory.TryAdd(secondary);
            yield return Tap(Key.Digit2);
            Assert.AreEqual(1, player.Shooter.ActiveSlot, "按 2 切到副武器");
            yield return Tap(Key.C);
            Assert.IsTrue(player.Shooter.IsHolstered, "按 C 收起武器");
            yield return Tap(Key.Digit1);
            Assert.AreEqual(0, player.Shooter.ActiveSlot, "按 1 切回主武器");
            Assert.IsFalse(player.Shooter.IsHolstered, "切武器会取出武器");
        }

        [UnityTest]
        public IEnumerator Key3_UsesAutoAssignedBandage_StopsBleeding()
        {
            ItemDefinition bandage = null;
#if UNITY_EDITOR
            bandage = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDefinition>(BandagePath);
#endif
            Assert.IsNotNull(bandage);
            Assert.IsTrue(bandage.IsUsable, "绷带应已填好使用数值（Wilderness/Setup/Update Player Controls）");
            if (player.Bleeding == null) Assert.Ignore("Map01 玩家没有流血组件");

            Assert.IsTrue(player.Inventory.Inventory.TryAdd(bandage));
            yield return null;
            var slot = -1;
            for (var i = PlayerItemUser.FirstItemSlot; i < player.Inventory.QuickSlots.Capacity; i++)
            {
                if (player.Inventory.QuickSlots[i] == bandage) slot = i;
            }

            Assert.GreaterOrEqual(slot, PlayerItemUser.FirstItemSlot, "新捡到的绷带应自动放进 3–8");
            player.Bleeding.Bleed();
            yield return Tap(Key.Digit1 + slot);
            Assert.IsTrue(player.ItemUser.IsUsing, "按对应数字键开始用绷带");
            yield return new WaitForSeconds(bandage.useDuration + 0.3f);
            Assert.IsFalse(player.ItemUser.IsUsing);
            Assert.IsFalse(player.Bleeding.IsBleeding, "绷带止血");
            Assert.AreEqual(0, player.Inventory.Inventory.CountOf(bandage), "用掉一个");
        }

        [UnityTest]
        public IEnumerator Cursor_ConfinedInGameplay_ReleasedByPauseReadingAndCrafting()
        {
            var authority = Object.FindAnyObjectByType<Map01Authority>();
            var flow = Object.FindAnyObjectByType<Map01RunFlow>();
            yield return null;
            AssertCursorInGameplay();

            flow.SetPaused(true);
            yield return null;
            AssertCursorReleased("暂停");
            flow.SetPaused(false);
            yield return null;
            AssertCursorInGameplay();

            flow.OpenReading("记录", "正文");
            yield return null;
            AssertCursorReleased("阅读");
            flow.CloseReading();
            yield return null;
            yield return null;
            AssertCursorInGameplay();

            flow.OpenCrafting(authority.FindStation(Map01Station.Workbench));
            yield return null;
            AssertCursorReleased("工作台");
            flow.CloseCrafting();
            yield return null;
            yield return null;
            AssertCursorInGameplay();
        }

        private void AssertCursorInGameplay()
        {
            var hud = player.GetComponent<PlayerStatusHud>();
            // 窗口失焦时（批处理可能如此）本来就该放开鼠标。
            Assert.AreEqual(Application.isFocused, hud.CrosshairActive, "可操作且有焦点时显示准星");
            Assert.AreEqual(Application.isFocused ? CursorLockMode.Confined : CursorLockMode.None, Cursor.lockState);
            Assert.AreEqual(!Application.isFocused, Cursor.visible);
        }

        private void AssertCursorReleased(string context)
        {
            Assert.IsFalse(player.GetComponent<PlayerStatusHud>().CrosshairActive, $"{context}时隐藏准星");
            Assert.AreEqual(CursorLockMode.None, Cursor.lockState, $"{context}时解锁鼠标");
            Assert.IsTrue(Cursor.visible, $"{context}时显示系统光标");
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
