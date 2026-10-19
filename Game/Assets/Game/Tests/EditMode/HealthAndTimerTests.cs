using NUnit.Framework;

namespace WildernessExtraction.Tests
{
    public sealed class HealthModelTests
    {
        [Test]
        public void Damage_ReducesHealth_AndClampsAtZero()
        {
            var health = new HealthModel(75f);
            Assert.AreEqual(25f, health.ApplyDamage(25f));
            Assert.AreEqual(50f, health.Current);
            Assert.AreEqual(50f, health.ApplyDamage(80f));
            Assert.AreEqual(0f, health.Current);
            Assert.IsTrue(health.IsDead);
        }

        [Test]
        public void Dead_IgnoresFurtherDamage()
        {
            var health = new HealthModel(20f);
            health.ApplyDamage(20f);
            Assert.AreEqual(0f, health.ApplyDamage(20f));
            Assert.AreEqual(0f, health.Current);
        }

        [Test]
        public void NonPositiveDamage_IsIgnored()
        {
            var health = new HealthModel(100f);
            Assert.AreEqual(0f, health.ApplyDamage(0f));
            Assert.AreEqual(0f, health.ApplyDamage(-5f));
            Assert.AreEqual(100f, health.Current);
        }

        [Test]
        public void PlannedNumbers_ThreeShotsKillEnemy_FiveHitsKillPlayer()
        {
            var enemy = new HealthModel(75f);
            enemy.ApplyDamage(25f);
            enemy.ApplyDamage(25f);
            Assert.IsFalse(enemy.IsDead);
            enemy.ApplyDamage(25f);
            Assert.IsTrue(enemy.IsDead);

            var player = new HealthModel(100f);
            for (var i = 0; i < 4; i++)
            {
                player.ApplyDamage(20f);
            }

            Assert.IsFalse(player.IsDead);
            player.ApplyDamage(20f);
            Assert.IsTrue(player.IsDead);
        }
    }

    public sealed class TimedProgressTests
    {
        [Test]
        public void CompletesOnceAfterDuration()
        {
            var search = new TimedProgress();
            search.Start(1.5f);
            Assert.IsFalse(search.Tick(1f));
            Assert.AreEqual(1f / 1.5f, search.Progress, 1e-4f);
            Assert.IsTrue(search.Tick(0.5f));
            Assert.IsFalse(search.IsRunning);
            Assert.IsFalse(search.Tick(1f));
        }

        [Test]
        public void Cancel_StopsAndResets()
        {
            var search = new TimedProgress();
            search.Start(1.5f);
            search.Tick(1f);
            search.Cancel();
            Assert.IsFalse(search.IsRunning);
            Assert.AreEqual(0f, search.Progress);
            Assert.IsFalse(search.Tick(5f));
        }
    }

    public sealed class ExtractionTimerTests
    {
        [Test]
        public void CompletesAfterContinuousStay()
        {
            var timer = new ExtractionTimer(3f);
            Assert.AreEqual(ExtractionTick.Started, timer.Tick(true, 1f));
            Assert.AreEqual(ExtractionTick.Progressing, timer.Tick(true, 1.5f));
            Assert.AreEqual(ExtractionTick.Completed, timer.Tick(true, 0.5f));
            Assert.IsTrue(timer.IsComplete);
            Assert.AreEqual(1f, timer.Progress);
        }

        [Test]
        public void LeavingZone_ResetsProgress()
        {
            var timer = new ExtractionTimer(3f);
            timer.Tick(true, 2.5f);
            Assert.AreEqual(ExtractionTick.Interrupted, timer.Tick(false, 0.1f));
            Assert.AreEqual(0f, timer.Elapsed);
            Assert.AreEqual(ExtractionTick.Started, timer.Tick(true, 2.5f));
            Assert.IsFalse(timer.IsComplete);
        }

        [Test]
        public void OutsideWithoutProgress_IsIdle()
        {
            var timer = new ExtractionTimer(3f);
            Assert.AreEqual(ExtractionTick.Idle, timer.Tick(false, 1f));
        }

        [Test]
        public void CompletesOnlyOnce()
        {
            var timer = new ExtractionTimer(3f);
            Assert.AreEqual(ExtractionTick.Completed, timer.Tick(true, 3f));
            Assert.AreEqual(ExtractionTick.Idle, timer.Tick(true, 1f));
            Assert.AreEqual(ExtractionTick.Idle, timer.Tick(false, 1f));
        }
    }
}
