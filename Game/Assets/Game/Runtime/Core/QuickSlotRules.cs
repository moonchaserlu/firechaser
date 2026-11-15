namespace WildernessExtraction
{
    public enum QuickSlotBindResult
    {
        Bound,
        /// <summary>材料、弹药、贵重品之类不能放进快捷栏。</summary>
        NotUsable,
        /// <summary>1、2 号格只放武器。</summary>
        WeaponSlotsOnly,
        /// <summary>这把武器角色用不了（没有对应的武器槽配置）。</summary>
        WeaponUnsupported,
        /// <summary>背包里没有这件东西。</summary>
        NotInBag,
        Invalid
    }

    /// <summary>
    /// 快捷栏的装卸规则（纯逻辑，便于 EditMode 测试）。快捷栏不会自动填：玩家把背包里的东西拖上来才装上，拖走就卸下。
    /// 下标 0、1（数字键 1、2）是主/副武器，只放武器，而且每把武器只能进它自己的那一格；2–7（数字键 3–8）放能用的消耗品。
    /// 同一件东西只占一格：拖到新格子时从旧格子挪过去。
    /// </summary>
    public static class QuickSlotRules
    {
        public const int WeaponSlots = 2;

        public static bool IsWeaponSlot(int index) => index >= 0 && index < WeaponSlots;

        /// <summary>
        /// 把背包里的 item 装到 index 格。weaponSlotOfItem 是这件东西对应的武器槽（0/1），不是角色的武器时传 -1。
        /// 武器不管拖到哪一格都进它自己的武器槽；返回实际装进的格子（失败为 -1）。
        /// </summary>
        public static QuickSlotBindResult Bind(QuickSlotBar bar, int index, ItemDefinition item, int countInBag, int weaponSlotOfItem, out int boundIndex)
        {
            boundIndex = -1;
            if (bar == null || item == null || index < 0 || index >= bar.Capacity) return QuickSlotBindResult.Invalid;
            if (countInBag <= 0) return QuickSlotBindResult.NotInBag;
            int target;
            if (weaponSlotOfItem >= 0 && weaponSlotOfItem < WeaponSlots)
            {
                target = weaponSlotOfItem;
            }
            else if (item.category == ItemCategory.Weapon)
            {
                return QuickSlotBindResult.WeaponUnsupported;
            }
            else if (!item.IsUsable)
            {
                return QuickSlotBindResult.NotUsable;
            }
            else if (IsWeaponSlot(index))
            {
                return QuickSlotBindResult.WeaponSlotsOnly;
            }
            else
            {
                target = index;
            }

            var previous = bar.IndexOf(item);
            if (previous >= 0 && previous != target) bar.Clear(previous);
            bar.Set(target, item);
            boundIndex = target;
            return QuickSlotBindResult.Bound;
        }

        /// <summary>
        /// 在快捷栏里把 from 格拖到 to 格：两格都是物品格时交换；武器格不能和物品格互换（武器格之间也不换，每把武器有自己的格）。
        /// </summary>
        public static bool Move(QuickSlotBar bar, int from, int to)
        {
            if (bar == null || from == to || from < 0 || to < 0 || from >= bar.Capacity || to >= bar.Capacity) return false;
            if (IsWeaponSlot(from) || IsWeaponSlot(to)) return false;
            return bar.Swap(from, to);
        }

        public static string Describe(QuickSlotBindResult result) => result switch
        {
            QuickSlotBindResult.NotUsable => Loc.T("只有武器和能用的东西能放进快捷栏"),
            QuickSlotBindResult.WeaponSlotsOnly => Loc.T("1、2 号格只放武器，能用的东西放 3–8"),
            QuickSlotBindResult.WeaponUnsupported => Loc.T("这把武器现在用不了"),
            QuickSlotBindResult.NotInBag => Loc.T("背包里没有这件东西"),
            _ => string.Empty
        };
    }
}
