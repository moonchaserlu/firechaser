using NUnit.Framework;

namespace WildernessExtraction.Tests
{
    public sealed class ReturnGateTests
    {
        [Test]
        public void NotArmed_UntilPlayerLeavesHomeFarEnough()
        {
            var gate = new ReturnGate(9f);
            Assert.IsFalse(gate.Update(3f));
            Assert.IsFalse(gate.Update(9f));
            Assert.IsFalse(gate.IsArmed);
            Assert.IsTrue(gate.Update(9.5f), "首次超过启用距离时返回 true");
            Assert.IsTrue(gate.IsArmed);
        }

        [Test]
        public void StaysArmed_AfterReturningHome()
        {
            var gate = new ReturnGate(9f);
            gate.Update(20f);
            Assert.IsFalse(gate.Update(0f), "只在首次启用时通知");
            Assert.IsTrue(gate.IsArmed);
        }

        [Test]
        public void Interrupt_ResetsProgress_OnlyWhileExtracting()
        {
            var timer = new ExtractionTimer(2f);
            Assert.IsFalse(timer.Interrupt(), "未开始时无需中断");
            timer.Tick(true, 1.5f);
            Assert.IsTrue(timer.Interrupt());
            Assert.AreEqual(0f, timer.Elapsed);
            Assert.AreEqual(ExtractionTick.Started, timer.Tick(true, 0.5f), "中断后需重新开始计时");
            timer.Tick(true, 2f);
            Assert.IsTrue(timer.IsComplete);
            Assert.IsFalse(timer.Interrupt(), "完成后不可被打断");
        }
    }
}
