using System;

namespace WildernessExtraction.Map01
{
    public enum ShopKind
    {
        Item,
        BackpackUpgrade
    }

    [Serializable]
    public sealed class ShopEntry
    {
        public string entryId;
        public string displayName;
        public int price;
        public ShopKind kind;
        public ItemDefinition item;
        [UnityEngine.Tooltip("背包升级增加的格数。")]
        public int extraSlots;
        [UnityEngine.Tooltip("最多可购买次数，0 = 不限。")]
        public int purchaseLimit;
    }

    /// <summary>安全屋账本：回收得到的价值、安全屋电量和救援条件。纯数据，由权威层修改。</summary>
    public sealed class SafehouseLedger
    {
        public SafehouseLedger(int powerRequiredForRescue)
        {
            if (powerRequiredForRescue <= 0) throw new ArgumentOutOfRangeException(nameof(powerRequiredForRescue));
            PowerRequiredForRescue = powerRequiredForRescue;
        }

        public int Credits { get; private set; }
        public int Power { get; private set; }
        public int PowerRequiredForRescue { get; }
        public bool RescueReady => Power >= PowerRequiredForRescue;

        public void AddCredits(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Credits += amount;
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || Credits < amount) return false;
            Credits -= amount;
            return true;
        }

        public void AddPower(int amount)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Power += amount;
        }
    }
}
