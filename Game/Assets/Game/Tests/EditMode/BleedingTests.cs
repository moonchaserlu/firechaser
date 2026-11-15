using NUnit.Framework;
using UnityEngine;

namespace WildernessExtraction.Tests
{
    public sealed class BleedingModelTests
    {
        [Test]
        public void RepeatedHits_RefreshDuration_DoNotStack()
        {
            var model = new BleedingModel(8f, 1f);
            model.Bleed();
            model.Tick(5f, false, false);
            Assert.AreEqual(3f, model.Remaining, 1e-4f);
            model.Bleed();
            Assert.AreEqual(8f, model.Remaining, 1e-4f, "刷新为完整时长，不叠加");
        }

        [Test]
        public void Drops_OncePerSecond_OnlyWhileMoving()
        {
            var model = new BleedingModel(8f, 1f);
            model.Bleed();
            var drops = 0;
            for (var i = 0; i < 60 * 3; i++)
            {
                if (model.Tick(1f / 60f, true, false)) drops++;
            }

            Assert.AreEqual(3, drops, 1);
            for (var i = 0; i < 60 * 2; i++)
            {
                Assert.IsFalse(model.Tick(1f / 60f, false, false), "静止不留血迹");
            }
        }

        [Test]
        public void InLight_NoDrops()
        {
            var model = new BleedingModel(8f, 1f);
            model.Bleed();
            for (var i = 0; i < 60 * 4; i++)
            {
                Assert.IsFalse(model.Tick(1f / 60f, true, true));
            }
        }

        [Test]
        public void StopsAfterDuration()
        {
            var model = new BleedingModel(8f, 1f);
            model.Bleed();
            model.Tick(8.1f, true, false);
            Assert.IsFalse(model.IsBleeding);
            Assert.IsFalse(model.Tick(2f, true, false));
        }
    }

    public sealed class BloodTrailRegistryTests
    {
        [Test]
        public void HardCap_EvictsOldest()
        {
            var registry = new BloodTrailRegistry { MaxMarks = 12, Lifetime = 15f };
            for (var i = 0; i < 20; i++) registry.Add(new Vector3(i, 0f, 0f), i * 0.1f);
            Assert.AreEqual(12, registry.Count);
            Assert.AreEqual(8f, registry.Marks[0].Position.x, 1e-4f, "最旧的 8 处被移除");
        }

        [Test]
        public void Marks_ExpireAfterLifetime()
        {
            var registry = new BloodTrailRegistry { MaxMarks = 12, Lifetime = 15f };
            registry.Add(Vector3.zero, 0f);
            registry.Add(Vector3.one, 10f);
            registry.Prune(15f);
            Assert.AreEqual(1, registry.Count);
            registry.Prune(25f);
            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void FindsOldestAfter_WithinRange_SkippingMasked()
        {
            var registry = new BloodTrailRegistry { MaxMarks = 12, Lifetime = 15f };
            registry.Add(new Vector3(20f, 0f, 0f), 1f); // 超出察觉范围
            registry.Add(new Vector3(5f, 0f, 0f), 2f);
            registry.Add(new Vector3(6f, 0f, 0f), 3f);
            registry.Add(new Vector3(7f, 0f, 0f), 4f);

            Assert.IsTrue(registry.TryFindOldestAfter(Vector3.zero, 12f, float.NegativeInfinity, 5f, null, out var first));
            Assert.AreEqual(2f, first.CreatedAt);
            Assert.IsTrue(registry.TryFindOldestAfter(Vector3.zero, 12f, first.CreatedAt, 5f, null, out var second));
            Assert.AreEqual(3f, second.CreatedAt, "按旧到新依次调查");

            Assert.IsTrue(registry.TryFindOldestAfter(Vector3.zero, 12f, 1f, 5f, p => p.x < 6.5f, out var unmasked));
            Assert.AreEqual(4f, unmasked.CreatedAt, "光内血迹气味被遮蔽");

            Assert.IsFalse(registry.TryFindOldestAfter(Vector3.zero, 12f, 4f, 5f, null, out _));
        }
    }
}
