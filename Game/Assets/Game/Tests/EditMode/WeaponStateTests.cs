using NUnit.Framework;

namespace WildernessExtraction.Tests
{
    public sealed class WeaponStateTests
    {
        // 策划初值：弹匣 8，射击间隔 0.25s，换弹 1.4s。
        private static WeaponState CreateDefault() => new WeaponState(8, 0.25f, 1.4f);

        [Test]
        public void StartsWithFullMagazine()
        {
            var weapon = CreateDefault();
            Assert.AreEqual(8, weapon.AmmoInMagazine);
            Assert.IsFalse(weapon.IsReloading);
        }

        [Test]
        public void Fire_ConsumesOneRound()
        {
            var weapon = CreateDefault();
            Assert.IsTrue(weapon.TryFire(0f));
            Assert.AreEqual(7, weapon.AmmoInMagazine);
        }

        [Test]
        public void Fire_RespectsFireInterval()
        {
            var weapon = CreateDefault();
            Assert.IsTrue(weapon.TryFire(0f));
            Assert.IsFalse(weapon.TryFire(0.2f));
            Assert.AreEqual(7, weapon.AmmoInMagazine);
            Assert.IsTrue(weapon.TryFire(0.25f));
            Assert.AreEqual(6, weapon.AmmoInMagazine);
        }

        [Test]
        public void Fire_FailsWhenEmpty()
        {
            var weapon = CreateDefault();
            var time = 0f;
            for (var i = 0; i < 8; i++, time += 0.25f)
            {
                Assert.IsTrue(weapon.TryFire(time));
            }

            Assert.AreEqual(0, weapon.AmmoInMagazine);
            Assert.IsFalse(weapon.TryFire(time + 10f));
        }

        [Test]
        public void Reload_RefillsAfterDuration_AndBlocksFiring()
        {
            var weapon = CreateDefault();
            weapon.TryFire(0f);
            weapon.TryFire(0.25f);

            Assert.IsTrue(weapon.TryStartReload(1f));
            Assert.IsTrue(weapon.IsReloading);
            Assert.IsFalse(weapon.TryFire(1.5f));
            Assert.AreEqual(0.5f, weapon.ReloadProgress(1.7f), 1e-4f);

            weapon.Tick(2.35f);
            Assert.IsTrue(weapon.IsReloading);
            weapon.Tick(2.45f);
            Assert.IsFalse(weapon.IsReloading);
            Assert.AreEqual(8, weapon.AmmoInMagazine);
            Assert.IsTrue(weapon.TryFire(2.45f));
        }

        [Test]
        public void Reload_RejectedWhenFull_OrAlreadyReloading()
        {
            var weapon = CreateDefault();
            Assert.IsFalse(weapon.TryStartReload(0f));
            weapon.TryFire(0f);
            Assert.IsTrue(weapon.TryStartReload(0.1f));
            Assert.IsFalse(weapon.TryStartReload(0.2f));
        }

        [Test]
        public void Reload_UsesOnlyAvailableReserveRounds()
        {
            var weapon = CreateDefault();
            weapon.TryFire(0f);
            weapon.TryFire(.25f);
            weapon.TryFire(.5f);
            Assert.IsTrue(weapon.TryStartReload(1f, 2));
            weapon.Tick(2.4f);
            Assert.AreEqual(7, weapon.AmmoInMagazine);
        }
    }
}
