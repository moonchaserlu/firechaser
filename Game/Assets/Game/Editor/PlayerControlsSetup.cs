using UnityEditor;
using UnityEngine;

namespace WildernessExtraction.EditorTools
{
    /// <summary>
    /// 键鼠操作一键配置：补齐输入动作，并给医疗/食物物品填上使用数值（占位，策划可在物品资产上直接改）。
    /// 物品已有 useDuration 时视为已调过，不覆盖。
    /// </summary>
    public static class PlayerControlsSetup
    {
        private static readonly (string itemId, float duration, float heal, bool stopsBleeding)[] UseDefaults =
        {
            ("dirty_bandage", 2f, 0f, true),
            ("first_aid_spray", 3f, 40f, true),
            ("canned_food", 2.5f, 15f, false),
        };

        [MenuItem("Wilderness/Setup/Update Player Controls")]
        private static void UpdateFromMenu()
        {
            var inputChanged = InputActionsSetup.EnsureSliceActions();
            var items = ApplyItemUseDefaults();
            Debug.Log($"[Wilderness] 玩家操作配置完成：输入动作{(inputChanged ? "已补齐" : "无需修改")}，填写使用数值的物品 {items} 个。");
        }

        /// <summary>返回本次写入的物品数量。</summary>
        public static int ApplyItemUseDefaults()
        {
            var written = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item == null || item.useDuration > 0f) continue;
                foreach (var entry in UseDefaults)
                {
                    if (item.itemId != entry.itemId) continue;
                    var serialized = new SerializedObject(item);
                    serialized.FindProperty("useDuration").floatValue = entry.duration;
                    serialized.FindProperty("healAmount").floatValue = entry.heal;
                    serialized.FindProperty("stopsBleeding").boolValue = entry.stopsBleeding;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(item);
                    written++;
                }
            }

            if (written > 0) AssetDatabase.SaveAssets();
            return written;
        }
    }
}
