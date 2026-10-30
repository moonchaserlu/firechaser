namespace WildernessExtraction
{
    public enum ItemUseCheck
    {
        Ok,
        NotUsable,
        NoneLeft,
        /// <summary>生命已满且没有流血，使用不会产生效果。</summary>
        NotNeeded
    }

    /// <summary>判断一件物品此刻能否使用（快捷栏 3–8、背包内 U）。纯逻辑，便于 EditMode 测试。</summary>
    public static class ItemUseRules
    {
        public static ItemUseCheck Check(ItemDefinition item, int count, float health, float maxHealth, bool bleeding)
        {
            if (item == null || !item.IsUsable) return ItemUseCheck.NotUsable;
            if (count <= 0) return ItemUseCheck.NoneLeft;
            var heals = item.healAmount > 0f && health < maxHealth;
            var stopsBleeding = item.stopsBleeding && bleeding;
            return heals || stopsBleeding ? ItemUseCheck.Ok : ItemUseCheck.NotNeeded;
        }

        public static string Describe(ItemUseCheck check, ItemDefinition item)
        {
            var name = item != null ? item.displayName : "该物品";
            switch (check)
            {
                case ItemUseCheck.NotUsable:
                    return $"{name} 无法使用";
                case ItemUseCheck.NoneLeft:
                    return $"背包里没有 {name}";
                case ItemUseCheck.NotNeeded:
                    return item != null && item.stopsBleeding && item.healAmount <= 0f ? "没有在流血" : "生命已满";
                default:
                    return string.Empty;
            }
        }
    }
}
