using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WildernessExtraction.EditorTools
{
    /// <summary>
    /// 为现有 InputSystem_Actions 的 Player 表补齐切片需要的动作（Aim/Dodge/Reload/Pause），
    /// 以及参考鸭科夫的键鼠操作（奔跑、精确瞄准、1/2 切武器、滚轮、V 近战、C 收枪、U 使用、X 丢弃、F 交互）。
    /// 通过 Input System API 读写，与 Input Actions 编辑器保存方式一致；已有动作和绑定不会被改动，只做追加。
    /// </summary>
    public static class InputActionsSetup
    {
        public const string AssetPath = "Assets/InputSystem_Actions.inputactions";
        private const string MapName = "Player";
        private const string KeyboardMouseScheme = "Keyboard&Mouse";

        [MenuItem("Wilderness/Setup/Update Input Actions")]
        private static void UpdateFromMenu()
        {
            var changed = EnsureSliceActions();
            Debug.Log(changed
                ? "[Wilderness] 已向 InputSystem_Actions/Player 补齐缺少的动作与绑定。"
                : "[Wilderness] InputSystem_Actions 已包含切片所需动作，无需修改。");
        }

        /// <summary>返回是否修改了资产。</summary>
        public static bool EnsureSliceActions()
        {
            var asset = InputActionAsset.FromJson(File.ReadAllText(AssetPath));
            try
            {
                var map = asset.FindActionMap(MapName, true);
                var changed = false;
                changed |= EnsureAction(map, "Aim", InputActionType.Value, "Vector2", "<Pointer>/position");
                changed |= EnsureAction(map, "Dodge", InputActionType.Button, null, "<Keyboard>/space");
                changed |= EnsureAction(map, "Reload", InputActionType.Button, null, "<Keyboard>/r");
                changed |= EnsureAction(map, "Pause", InputActionType.Button, null, "<Keyboard>/escape");
                changed |= EnsureAction(map, "Inventory", InputActionType.Button, null, "<Keyboard>/tab");
                changed |= EnsureAction(map, "BuildFire", InputActionType.Button, null, "<Keyboard>/q");
                changed |= EnsureAction(map, "CancelAction", InputActionType.Button, null, "<Mouse>/rightButton");
                changed |= EnsureAction(map, "Sprint", InputActionType.Button, null, "<Keyboard>/leftShift");
                changed |= EnsureAction(map, "AimDownSights", InputActionType.Button, null, "<Mouse>/rightButton");
                changed |= EnsureAction(map, "Melee", InputActionType.Button, null, "<Keyboard>/v");
                changed |= EnsureAction(map, "Holster", InputActionType.Button, null, "<Keyboard>/c");
                changed |= EnsureAction(map, "WeaponPrimary", InputActionType.Button, null, "<Keyboard>/1");
                changed |= EnsureAction(map, "WeaponSecondary", InputActionType.Button, null, "<Keyboard>/2");
                changed |= EnsureAction(map, "CycleWeapon", InputActionType.Value, "Axis", "<Mouse>/scroll/y");
                changed |= EnsureAction(map, "UseItem", InputActionType.Button, null, "<Keyboard>/u");
                changed |= EnsureAction(map, "DropItem", InputActionType.Button, null, "<Keyboard>/x");
                // 交互键改为 F，原有的 E 保留为备用键。
                changed |= EnsureBinding(map.FindAction("Interact", true), "<Keyboard>/f");
                if (!changed)
                {
                    return false;
                }

                File.WriteAllText(AssetPath, asset.ToJson());
                AssetDatabase.ImportAsset(AssetPath);
                return true;
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        private static bool EnsureBinding(InputAction action, string bindingPath)
        {
            foreach (var binding in action.bindings)
            {
                if (binding.path == bindingPath)
                {
                    return false;
                }
            }

            action.AddBinding(bindingPath, groups: KeyboardMouseScheme);
            return true;
        }

        private static bool EnsureAction(InputActionMap map, string actionName, InputActionType type, string controlLayout, string bindingPath)
        {
            if (map.FindAction(actionName, false) != null)
            {
                return false;
            }

            var action = map.AddAction(actionName, type, expectedControlLayout: controlLayout);
            action.AddBinding(bindingPath, groups: KeyboardMouseScheme);
            return true;
        }
    }
}
