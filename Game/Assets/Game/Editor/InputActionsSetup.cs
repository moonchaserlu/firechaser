using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WildernessExtraction.EditorTools
{
    /// <summary>
    /// 为现有 InputSystem_Actions 的 Player 表补齐切片需要的动作（Aim/Dodge/Reload/Pause）。
    /// 通过 Input System API 读写，与 Input Actions 编辑器保存方式一致；已有动作不会被改动。
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
                ? "[Wilderness] 已向 InputSystem_Actions/Player 补齐 Aim、Dodge、Reload、Pause。"
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
