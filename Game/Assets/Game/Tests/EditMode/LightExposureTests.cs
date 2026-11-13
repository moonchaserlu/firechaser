using NUnit.Framework;
using UnityEngine;

namespace WildernessExtraction.Tests
{
    public sealed class LightExposureModelTests
    {
        private const float Warning = 1.5f;
        private const float Dps = 20f;

        [Test]
        public void InLight_StaysSafe_NoDamage()
        {
            var model = new LightExposureModel(Warning, Dps);
            Assert.AreEqual(0f, model.Tick(true, 10f));
            Assert.AreEqual(LightExposureState.Safe, model.State);
        }

        [Test]
        public void WarningPeriod_DealsNoDamage()
        {
            var model = new LightExposureModel(Warning, Dps);
            Assert.AreEqual(0f, model.Tick(false, 1.0f));
            Assert.AreEqual(LightExposureState.Warning, model.State);
            Assert.AreEqual(0.5f, model.WarningRemaining, 1e-4f);
        }

        [Test]
        public void AfterWarning_DamagesPerSecond()
        {
            var model = new LightExposureModel(Warning, Dps);
            model.Tick(false, Warning);
            Assert.AreEqual(LightExposureState.Exposed, model.State);
            Assert.AreEqual(Dps, model.Tick(false, 1f), 1e-4f);
        }

        [Test]
        public void FrameCrossingWarningEnd_OnlyDamagesExcess()
        {
            var model = new LightExposureModel(Warning, Dps);
            model.Tick(false, 1.0f);
            Assert.AreEqual(0.5f * Dps, model.Tick(false, 1.0f), 1e-4f);
        }

        [Test]
        public void FullHealth_DiesInAboutSixAndHalfSeconds()
        {
            var model = new LightExposureModel(Warning, Dps);
            var health = new HealthModel(100f);
            var elapsed = 0f;
            const float dt = 1f / 60f;
            while (!health.IsDead && elapsed < 20f)
            {
                health.ApplyDamage(model.Tick(false, dt));
                elapsed += dt;
            }

            Assert.AreEqual(6.5f, elapsed, 0.05f);
        }

        [Test]
        public void ReturningToLight_StopsDamage_AndResetsTimer()
        {
            var model = new LightExposureModel(Warning, Dps);
            model.Tick(false, 3f);
            Assert.AreEqual(0f, model.Tick(true, 0.016f));
            Assert.AreEqual(LightExposureState.Safe, model.State);
            Assert.AreEqual(0f, model.DarkTime);

            // 再次离光需要重新经过完整预警。
            Assert.AreEqual(0f, model.Tick(false, 1.4f));
            Assert.AreEqual(LightExposureState.Warning, model.State);
        }
    }

    public sealed class LightRegistryTests
    {
        private sealed class FakeArea : ISafeLightArea
        {
            public Vector3 Center;
            public float Radius;
            public bool IsLit { get; set; } = true;

            public bool Contains(Vector3 worldPosition)
            {
                var delta = worldPosition - Center;
                delta.y = 0f;
                return delta.sqrMagnitude <= Radius * Radius;
            }
        }

        [Test]
        public void EmptyRegistry_IsDark()
        {
            Assert.IsFalse(new LightRegistry().IsLit(Vector3.zero));
        }

        [Test]
        public void InsideRadius_IsLit_EdgeInclusive_IgnoresHeight()
        {
            var registry = new LightRegistry();
            registry.Register(new FakeArea { Center = Vector3.zero, Radius = 6f });
            Assert.IsTrue(registry.IsLit(new Vector3(6f, 3f, 0f)));
            Assert.IsFalse(registry.IsLit(new Vector3(6.01f, 0f, 0f)));
        }

        [Test]
        public void OverlappingLights_DoNotStack_AndOneGoingOutKeepsOther()
        {
            var registry = new LightRegistry();
            var a = new FakeArea { Center = Vector3.zero, Radius = 4f };
            var b = new FakeArea { Center = new Vector3(3f, 0f, 0f), Radius = 4f };
            registry.Register(a);
            registry.Register(b);
            registry.Register(a);
            Assert.AreEqual(2, registry.Count, "重复登记不应产生重复条目");

            var overlap = new Vector3(1.5f, 0f, 0f);
            Assert.IsTrue(registry.IsLit(overlap));
            a.IsLit = false;
            Assert.IsTrue(registry.IsLit(overlap));
            b.IsLit = false;
            Assert.IsFalse(registry.IsLit(overlap));
        }

        [Test]
        public void UnregisteredLight_NoLongerProtects()
        {
            var registry = new LightRegistry();
            var area = new FakeArea { Center = Vector3.zero, Radius = 4f };
            registry.Register(area);
            registry.Unregister(area);
            Assert.IsFalse(registry.IsLit(Vector3.zero));
            Assert.AreEqual(0, registry.Count);
        }
    }
}
