using NUnit.Framework;

namespace WildernessExtraction.Tests
{
    public sealed class StaminaModelTests
    {
        // 策划初值：最大 100，翻滚消耗 25，恢复 20/s，停止消耗 0.5s 后开始恢复。
        private static StaminaModel CreateDefault() => new StaminaModel(100f, 20f, 0.5f);

        [Test]
        public void StartsFull()
        {
            var stamina = CreateDefault();
            Assert.AreEqual(100f, stamina.Current);
            Assert.AreEqual(1f, stamina.Normalized);
        }

        [Test]
        public void TryConsume_DeductsCost()
        {
            var stamina = CreateDefault();
            Assert.IsTrue(stamina.TryConsume(25f));
            Assert.AreEqual(75f, stamina.Current, 1e-4f);
        }

        [Test]
        public void TryConsume_FailsWhenInsufficient_AndDoesNotChangeValue()
        {
            var stamina = CreateDefault();
            for (var i = 0; i < 4; i++)
            {
                Assert.IsTrue(stamina.TryConsume(25f));
            }

            Assert.IsFalse(stamina.TryConsume(25f));
            Assert.AreEqual(0f, stamina.Current, 1e-4f);
        }

        [Test]
        public void DoesNotRegenerateDuringDelay()
        {
            var stamina = CreateDefault();
            stamina.TryConsume(25f);
            stamina.Tick(0.49f);
            Assert.AreEqual(75f, stamina.Current, 1e-4f);
        }

        [Test]
        public void RegeneratesAtRateAfterDelay_IncludingPartialFrame()
        {
            var stamina = CreateDefault();
            stamina.TryConsume(25f);
            stamina.Tick(0.4f);
            stamina.Tick(0.2f); // 0.1s 等待 + 0.1s 恢复
            Assert.AreEqual(77f, stamina.Current, 1e-3f);
            stamina.Tick(0.5f);
            Assert.AreEqual(87f, stamina.Current, 1e-3f);
        }

        [Test]
        public void NewConsumeRestartsDelay()
        {
            var stamina = CreateDefault();
            stamina.TryConsume(25f);
            stamina.Tick(1f); // +10
            stamina.TryConsume(25f);
            stamina.Tick(0.3f);
            Assert.AreEqual(60f, stamina.Current, 1e-3f);
        }

        [Test]
        public void RegenerationClampsAtMax()
        {
            var stamina = CreateDefault();
            stamina.TryConsume(10f);
            stamina.Tick(10f);
            Assert.AreEqual(100f, stamina.Current, 1e-4f);
        }
    }
}
