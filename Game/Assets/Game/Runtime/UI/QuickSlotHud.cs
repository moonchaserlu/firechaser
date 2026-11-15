using System;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 底部快捷栏的共享布局与“拖上去装备”的入口。快捷栏由 <see cref="PlayerStatusHud"/> 画；
    /// 背包面板（Tab 物品页、搜箱子面板）每帧调 <see cref="MarkEditing"/>，表示此时可以往快捷栏拖东西、也能从快捷栏拖走卸下。
    /// 坐标都是 IMGUI 坐标（左上原点）。
    /// </summary>
    public static class QuickSlotHud
    {
        public const float SlotSize = 62f;
        public const float SlotSpacing = 6f;
        public const float BottomMargin = 16f;

        private static int editingFrame = -10;

        /// <summary>装上、卸下或装不上时给玩家看的一句话（PlayerStatusHud 显示）。</summary>
        public static event Action<string> Notice;

        /// <summary>背包类面板开着：快捷栏可编辑。</summary>
        public static bool Editing => Time.frameCount - editingFrame <= 1;

        public static void MarkEditing() => editingFrame = Time.frameCount;

        public static Rect SlotRect(int index, int count)
        {
            var x0 = Screen.width * 0.5f - (count * SlotSize + (count - 1) * SlotSpacing) * 0.5f;
            return new Rect(x0 + index * (SlotSize + SlotSpacing), Screen.height - BottomMargin - SlotSize, SlotSize, SlotSize);
        }

        /// <summary>指针下的快捷栏格子；不在任何格子上返回 -1（格子之间的缝也算最近的那格）。</summary>
        public static int SlotAt(Vector2 guiPosition, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var rect = SlotRect(i, count);
                rect.xMin -= SlotSpacing * 0.5f;
                rect.xMax += SlotSpacing * 0.5f;
                rect.yMin -= 4f;
                if (rect.Contains(guiPosition)) return i;
            }

            return -1;
        }

        /// <summary>把背包里的 item 拖到快捷栏第 index 格。</summary>
        public static bool Drop(PlayerCharacter player, int index, ItemDefinition item)
        {
            if (player == null || item == null) return false;
            var inventory = player.Inventory;
            var shooter = player.Shooter;
            var result = QuickSlotRules.Bind(inventory.QuickSlots, index, item, inventory.Inventory.CountOf(item),
                shooter != null ? shooter.WeaponSlotOf(item) : -1, out var bound);
            Notice?.Invoke(result == QuickSlotBindResult.Bound ? Loc.F("{0} 装到快捷栏 {1}", item.DisplayName, bound + 1) : QuickSlotRules.Describe(result));
            return result == QuickSlotBindResult.Bound;
        }

        /// <summary>从快捷栏卸下第 index 格（东西还在背包里）。</summary>
        public static void Unbind(PlayerCharacter player, int index)
        {
            if (player == null) return;
            var bar = player.Inventory.QuickSlots;
            var item = bar[index];
            if (item == null) return;
            bar.Clear(index);
            Notice?.Invoke(Loc.F("{0} 已从快捷栏卸下", item.DisplayName));
        }
    }
}
