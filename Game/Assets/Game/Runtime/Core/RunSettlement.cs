using System.Collections.Generic;

namespace WildernessExtraction
{
    public enum RunOutcome
    {
        Extracted,
        Died
    }

    public sealed class RunResult
    {
        public RunResult(RunOutcome outcome, List<ItemDefinition> items)
        {
            Outcome = outcome;
            Items = items;
            foreach (var item in items)
            {
                Value += item.stashValue;
            }
        }

        public RunOutcome Outcome { get; }
        /// <summary>成功时为带回的物品，死亡时为丢失的物品。</summary>
        public IReadOnlyList<ItemDefinition> Items { get; }
        public int Value { get; }
    }

    /// <summary>结算规则：成功撤离把背包累加进仓库后清空；死亡只清空背包。</summary>
    public static class RunSettlement
    {
        public static RunResult Extract(Inventory inventory, Stash stash)
        {
            var items = inventory.TakeAll();
            stash.Deposit(items);
            return new RunResult(RunOutcome.Extracted, items);
        }

        public static RunResult Die(Inventory inventory)
        {
            return new RunResult(RunOutcome.Died, inventory.TakeAll());
        }
    }
}
