using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WildernessExtraction
{
    /// <summary>
    /// 从工程的 InputSystem_Actions 资产读取玩家输入。资产缺少某个动作时，
    /// 使用代码默认绑定并给出警告（运行 Wilderness/Setup/Update Input Actions 可补齐）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private string actionMapName = "Player";

        private readonly List<InputAction> fallbackActions = new List<InputAction>();
        private InputActionAsset runtimeActions;
        private InputActionMap actionMap;
        private InputAction move;
        private InputAction aim;
        private InputAction attack;
        private InputAction dodge;
        private InputAction reload;
        private InputAction interact;
        private InputAction pause;
        private InputAction inventory;
        private InputAction buildFire;
        private InputAction cancelAction;
        private InputAction sprint;
        private InputAction aimDownSights;
        private InputAction melee;
        private InputAction holster;
        private InputAction weaponPrimary;
        private InputAction weaponSecondary;
        private InputAction cycleWeapon;
        private InputAction useItem;
        private InputAction dropItem;

        /// <summary>暂停或结算时为 false：移动、射击、翻滚、换弹、交互均返回空输入。</summary>
        public bool GameplayEnabled { get; private set; } = true;

        /// <summary>动作已解析（Awake 之后恒为 true；其他组件在本组件 Awake 前访问时会先完成解析）。</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>输入资产里缺少、改用代码默认绑定的动作数量；为 0 表示全部动作来自 InputSystem_Actions。</summary>
        public int FallbackActionCount => fallbackActions.Count;

        public Vector2 Move => GameplayEnabled ? Action(ref move).ReadValue<Vector2>() : Vector2.zero;
        public Vector2 AimScreenPosition => Action(ref aim).ReadValue<Vector2>();
        /// <summary>为 true 时左键不触发攻击（建火预览、确认放置期间）。</summary>
        public bool AttackBlocked { get; set; }
        public bool AttackHeld => GameplayEnabled && !AttackBlocked && Action(ref attack).IsPressed();
        /// <summary>左键本帧按下，不受 AttackBlocked 限制，用于确认放置。</summary>
        public bool AttackPressedThisFrame => GameplayEnabled && Action(ref attack).WasPressedThisFrame();
        /// <summary>左键物理按住状态，不受任何开关限制。</summary>
        public bool AttackButtonDown => Action(ref attack).IsPressed();
        public bool BuildFirePressed => GameplayEnabled && Action(ref buildFire).WasPressedThisFrame();
        /// <summary>右键：取消当前预览或建造。</summary>
        public bool CancelActionPressed => GameplayEnabled && Action(ref cancelAction).WasPressedThisFrame();
        public bool DodgePressed => GameplayEnabled && Action(ref dodge).WasPressedThisFrame();
        public bool ReloadPressed => GameplayEnabled && Action(ref reload).WasPressedThisFrame();
        public bool InteractPressed => GameplayEnabled && Action(ref interact).WasPressedThisFrame();
        /// <summary>按住 Shift 奔跑。</summary>
        public bool SprintHeld => GameplayEnabled && Action(ref sprint).IsPressed();
        /// <summary>按住右键精确瞄准；建火预览期间右键只用于取消，不算瞄准。</summary>
        public bool AimDownSightsHeld => GameplayEnabled && !AttackBlocked && Action(ref aimDownSights).IsPressed();
        public bool MeleePressed => GameplayEnabled && Action(ref melee).WasPressedThisFrame();
        public bool HolsterPressed => GameplayEnabled && Action(ref holster).WasPressedThisFrame();
        /// <summary>背包内使用选中物品（U）；不受 GameplayEnabled 限制，背包打开时也能用。</summary>
        public bool UseItemPressed => Action(ref useItem).WasPressedThisFrame();
        /// <summary>背包内丢弃选中物品（X）；不受 GameplayEnabled 限制。</summary>
        public bool DropItemPressed => Action(ref dropItem).WasPressedThisFrame();

        /// <summary>为 true 时数字键让给其他系统（例如站在商店前时 1–4 用于购买），不切武器、不用快捷物品。</summary>
        public bool HotkeysSuppressed { get; set; }

        /// <summary>本帧按下的武器槽：1 → 0（主武器），2 → 1（副武器），未按为 -1。</summary>
        public int WeaponSlotPressed
        {
            get
            {
                if (!GameplayEnabled || HotkeysSuppressed) return -1;
                if (Action(ref weaponPrimary).WasPressedThisFrame()) return 0;
                if (Action(ref weaponSecondary).WasPressedThisFrame()) return 1;
                return -1;
            }
        }

        /// <summary>滚轮切换武器：向上 +1、向下 -1、未滚动 0。</summary>
        public int CycleWeaponDirection
        {
            get
            {
                if (!GameplayEnabled) return 0;
                var value = Action(ref cycleWeapon).ReadValue<float>();
                return value > 0.01f ? 1 : value < -0.01f ? -1 : 0;
            }
        }

        /// <summary>不受 GameplayEnabled 限制的交互键（F，E 备用），用于结算界面确认。</summary>
        public bool ConfirmPressed => Action(ref interact).WasPressedThisFrame();
        public bool PausePressed => Action(ref pause).WasPressedThisFrame();
        /// <summary>背包开关不受 GameplayEnabled 限制，确保面板打开后仍可用 Tab 关闭。</summary>
        public bool InventoryPressed => Action(ref inventory).WasPressedThisFrame();
        /// <summary>
        /// 本帧按下的数字键 1–8，对应快捷栏下标 0–7（0、1 是主/副武器，2–7 是快捷物品）。
        /// 不受 GameplayEnabled 限制，背包打开时用于绑定快捷键。
        /// </summary>
        public int QuickSlotPressed => HotkeysSuppressed ? -1 : DigitPressedThisFrame(8);

        /// <summary>任一键盘本帧按下的数字键 1..max（返回 0..max-1），未按为 -1。遍历全部键盘设备，不依赖 Keyboard.current（多键盘时 current 不一定是收到按键的那块）。</summary>
        public static int DigitPressedThisFrame(int max)
        {
            foreach (var device in InputSystem.devices)
            {
                if (!(device is Keyboard keyboard)) continue;
                for (var i = 0; i < max && i < 9; i++)
                {
                    if (keyboard[Key.Digit1 + i].wasPressedThisFrame) return i;
                }
            }

            return -1;
        }

        public void SetGameplayEnabled(bool enabled)
        {
            GameplayEnabled = enabled;
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private InputAction Action(ref InputAction field)
        {
            if (field == null)
            {
                EnsureInitialized();
            }

            return field;
        }

        private void EnsureInitialized()
        {
            if (IsInitialized)
            {
                return;
            }

            if (actions != null)
            {
                // 不用 Object.Instantiate：InputSystem_Actions 同时是 project-wide actions，Input System 启动时已启用它，
                // Instantiate 出的副本会带着与原资产共享状态相关的映射信息，Enable 时触发
                // "Map must be contained in state / Map index out of range"。经 JSON 复制得到状态完全独立的新资产。
                runtimeActions = InputActionAsset.FromJson(actions.ToJson());
                runtimeActions.name = actions.name + " (Runtime)";
                actionMap = runtimeActions.FindActionMap(actionMapName, false);
            }

            if (actionMap == null)
            {
                Debug.LogWarning($"[PlayerInputReader] 未找到动作表 '{actionMapName}'，全部使用代码默认绑定。", this);
            }

            move = actionMap?.FindAction("Move", false) ?? CreateFallbackMove();
            aim = Resolve("Aim", InputActionType.Value, "<Pointer>/position", "Vector2");
            attack = Resolve("Attack", InputActionType.Button, "<Mouse>/leftButton");
            dodge = Resolve("Dodge", InputActionType.Button, "<Keyboard>/space");
            reload = Resolve("Reload", InputActionType.Button, "<Keyboard>/r");
            interact = Resolve("Interact", InputActionType.Button, "<Keyboard>/f");
            // 交互键参考鸭科夫改为 F，E 保留为备用键；旧输入资产只有 E 时在运行时副本上补 F。
            EnsureBinding(interact, "<Keyboard>/f");
            EnsureBinding(interact, "<Keyboard>/e");
            pause = Resolve("Pause", InputActionType.Button, "<Keyboard>/escape");
            inventory = Resolve("Inventory", InputActionType.Button, "<Keyboard>/tab");
            buildFire = Resolve("BuildFire", InputActionType.Button, "<Keyboard>/q");
            cancelAction = Resolve("CancelAction", InputActionType.Button, "<Mouse>/rightButton");
            sprint = Resolve("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            aimDownSights = Resolve("AimDownSights", InputActionType.Button, "<Mouse>/rightButton");
            melee = Resolve("Melee", InputActionType.Button, "<Keyboard>/v");
            holster = Resolve("Holster", InputActionType.Button, "<Keyboard>/c");
            weaponPrimary = Resolve("WeaponPrimary", InputActionType.Button, "<Keyboard>/1");
            weaponSecondary = Resolve("WeaponSecondary", InputActionType.Button, "<Keyboard>/2");
            cycleWeapon = Resolve("CycleWeapon", InputActionType.Value, "<Mouse>/scroll/y", "Axis");
            useItem = Resolve("UseItem", InputActionType.Button, "<Keyboard>/u");
            dropItem = Resolve("DropItem", InputActionType.Button, "<Keyboard>/x");
            IsInitialized = true;
        }

        private void OnEnable()
        {
            EnsureInitialized();
            actionMap?.Enable();
            foreach (var action in fallbackActions)
            {
                action.Enable();
            }
        }

        private void OnDisable()
        {
            actionMap?.Disable();
            foreach (var action in fallbackActions)
            {
                action.Disable();
            }
        }

        private void OnDestroy()
        {
            foreach (var action in fallbackActions)
            {
                action.Dispose();
            }

            fallbackActions.Clear();
            if (runtimeActions != null)
            {
                runtimeActions.Disable();
                Destroy(runtimeActions);
                runtimeActions = null;
            }

            actionMap = null;
            move = aim = attack = dodge = reload = interact = pause = inventory = buildFire = cancelAction = null;
            sprint = aimDownSights = melee = holster = weaponPrimary = weaponSecondary = cycleWeapon = useItem = dropItem = null;
            IsInitialized = false;
        }

        private InputAction Resolve(string actionName, InputActionType type, string binding, string controlType = null)
        {
            var action = actionMap?.FindAction(actionName, false);
            if (action != null)
            {
                return action;
            }

            if (actionMap != null)
            {
                Debug.LogWarning($"[PlayerInputReader] 输入资产缺少 {actionMapName}/{actionName}，临时使用 {binding}。" +
                                 "请运行菜单 Wilderness/Setup/Update Input Actions。", this);
            }

            action = new InputAction(actionName, type, binding, expectedControlType: controlType);
            fallbackActions.Add(action);
            return action;
        }

        private static void EnsureBinding(InputAction action, string path)
        {
            foreach (var binding in action.bindings)
            {
                if (binding.path == path) return;
            }

            action.AddBinding(path);
        }

        private InputAction CreateFallbackMove()
        {
            var action = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            action.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            fallbackActions.Add(action);
            return action;
        }
    }
}
