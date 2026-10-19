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

        /// <summary>暂停或结算时为 false：移动、射击、翻滚、换弹、交互均返回空输入。</summary>
        public bool GameplayEnabled { get; private set; } = true;

        /// <summary>动作已解析（Awake 之后恒为 true；其他组件在本组件 Awake 前访问时会先完成解析）。</summary>
        public bool IsInitialized { get; private set; }

        public Vector2 Move => GameplayEnabled ? Action(ref move).ReadValue<Vector2>() : Vector2.zero;
        public Vector2 AimScreenPosition => Action(ref aim).ReadValue<Vector2>();
        public bool AttackHeld => GameplayEnabled && Action(ref attack).IsPressed();
        public bool DodgePressed => GameplayEnabled && Action(ref dodge).WasPressedThisFrame();
        public bool ReloadPressed => GameplayEnabled && Action(ref reload).WasPressedThisFrame();
        public bool InteractPressed => GameplayEnabled && Action(ref interact).WasPressedThisFrame();
        /// <summary>不受 GameplayEnabled 限制的 E 键，用于结算界面确认。</summary>
        public bool ConfirmPressed => Action(ref interact).WasPressedThisFrame();
        public bool PausePressed => Action(ref pause).WasPressedThisFrame();
        /// <summary>背包开关不受 GameplayEnabled 限制，确保面板打开后仍可用 Tab 关闭。</summary>
        public bool InventoryPressed => Action(ref inventory).WasPressedThisFrame();
        public int QuickSlotPressed
        {
            get
            {
                if (Keyboard.current == null) return -1;
                if (Keyboard.current.digit1Key.wasPressedThisFrame) return 0;
                if (Keyboard.current.digit2Key.wasPressedThisFrame) return 1;
                if (Keyboard.current.digit3Key.wasPressedThisFrame) return 2;
                if (Keyboard.current.digit4Key.wasPressedThisFrame) return 3;
                if (Keyboard.current.digit5Key.wasPressedThisFrame) return 4;
                if (Keyboard.current.digit6Key.wasPressedThisFrame) return 5;
                if (Keyboard.current.digit7Key.wasPressedThisFrame) return 6;
                if (Keyboard.current.digit8Key.wasPressedThisFrame) return 7;
                return -1;
            }
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
            interact = Resolve("Interact", InputActionType.Button, "<Keyboard>/e");
            pause = Resolve("Pause", InputActionType.Button, "<Keyboard>/escape");
            inventory = Resolve("Inventory", InputActionType.Button, "<Keyboard>/tab");
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
            move = aim = attack = dodge = reload = interact = pause = inventory = null;
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
