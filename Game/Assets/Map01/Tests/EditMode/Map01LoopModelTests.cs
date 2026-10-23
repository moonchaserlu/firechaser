using NUnit.Framework;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>阶段 4 纯规则：发电机状态机、撤离状态机、安全屋账本。</summary>
    public sealed class Map01LoopModelTests
    {
        [Test]
        public void Generator_RepairThenProducesCharge()
        {
            var generator = new GeneratorModel(4f, 10f, 2);
            Assert.AreEqual(GeneratorState.Unknown, generator.State);
            Assert.IsTrue(generator.Discover());
            Assert.AreEqual(GeneratorState.NeedsRepair, generator.State);
            Assert.IsFalse(generator.TryTakeCharge(), "未修好不能取电");

            Assert.IsTrue(generator.BeginRepair());
            Assert.IsFalse(generator.Tick(3f, true));
            Assert.IsTrue(generator.Tick(1.1f, true), "修满 4 秒完成");
            Assert.AreEqual(GeneratorState.Producing, generator.State);

            generator.Tick(25f, false);
            Assert.AreEqual(2, generator.StoredCharge, "存电不超过上限");
            Assert.AreEqual(GeneratorState.Operational, generator.State);
            Assert.IsTrue(generator.TryTakeCharge());
            Assert.AreEqual(1, generator.StoredCharge);
        }

        [Test]
        public void Generator_RepairCancelledWhenRepairerLeaves()
        {
            var generator = new GeneratorModel(4f, 10f, 2);
            generator.BeginRepair();
            generator.Tick(2f, true);
            generator.Tick(0.1f, false);
            Assert.IsFalse(generator.IsRepairing);
            Assert.AreEqual(GeneratorState.NeedsRepair, generator.State);
            Assert.AreEqual(0f, generator.RepairProgress);
        }

        [Test]
        public void Extraction_UnusableUntilActivated_ThenBoardsAndCompletes()
        {
            var extraction = new ExtractionModel("map01_extract_sw_01", 5f);
            Assert.AreEqual(ExtractionState.Candidate, extraction.State);
            Assert.AreEqual(ExtractionState.Candidate, extraction.Tick(10f, true, false), "救援未呼叫时站进去也不能撤离");

            Assert.IsTrue(extraction.Activate());
            Assert.AreEqual(ExtractionState.Boarding, extraction.Tick(0.1f, true, false));
            extraction.Tick(3f, true, false);
            Assert.AreEqual(ExtractionState.Completed, extraction.Tick(2.1f, true, false));
        }

        [Test]
        public void Extraction_InterruptsWithReason()
        {
            var extraction = new ExtractionModel("x", 5f);
            extraction.Activate();
            extraction.Tick(0.1f, true, false);
            Assert.AreEqual(ExtractionState.Interrupted, extraction.Tick(1f, true, true));
            Assert.AreEqual(ExtractionInterruptReason.MonsterNearby, extraction.LastInterrupt);
            Assert.AreEqual(0f, extraction.BoardingElapsed, "中断后重新计时");

            extraction.Tick(0.1f, true, false);
            Assert.AreEqual(ExtractionState.Interrupted, extraction.Tick(1f, false, false));
            Assert.AreEqual(ExtractionInterruptReason.LeftZone, extraction.LastInterrupt);
        }

        [Test]
        public void Ledger_CreditsAndRescueCondition()
        {
            var ledger = new SafehouseLedger(2);
            ledger.AddCredits(50);
            Assert.IsFalse(ledger.TrySpend(60));
            Assert.IsTrue(ledger.TrySpend(30));
            Assert.AreEqual(20, ledger.Credits);
            ledger.AddPower(1);
            Assert.IsFalse(ledger.RescueReady);
            ledger.AddPower(1);
            Assert.IsTrue(ledger.RescueReady);
        }

        [Test]
        public void Inventory_ExpandAddsSlots()
        {
            var inventory = new Inventory(8);
            Assert.IsTrue(inventory.Expand(4));
            Assert.AreEqual(12, inventory.Capacity);
            Assert.IsFalse(inventory.Expand(0));
        }
    }
}
