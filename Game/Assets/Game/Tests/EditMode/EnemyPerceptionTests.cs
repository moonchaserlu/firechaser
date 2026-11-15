using NUnit.Framework;
using UnityEngine;

namespace WildernessExtraction.Tests
{
    public sealed class EnemyPerceptionTests
    {
        private static bool Detect(Vector3 target, bool lineOfSight = true)
        {
            // 怪物在原点朝 +Z，视野 9 米、110°，近身 1.5 米。
            return EnemyPerception.CanDetect(Vector3.zero, Vector3.forward, target, 9f, 110f, 1.5f, lineOfSight);
        }

        [Test]
        public void InFrontWithinRange_Detected() => Assert.IsTrue(Detect(new Vector3(0f, 0f, 8f)));

        [Test]
        public void BeyondSightRange_NotDetected() => Assert.IsFalse(Detect(new Vector3(0f, 0f, 9.5f)));

        [Test]
        public void Behind_NotDetected() => Assert.IsFalse(Detect(new Vector3(0f, 0f, -4f)));

        [Test]
        public void OutsideCone_NotDetected()
        {
            // 与正前方夹角 60° > 55°。
            Assert.IsFalse(Detect(Quaternion.Euler(0f, 60f, 0f) * Vector3.forward * 5f));
            Assert.IsTrue(Detect(Quaternion.Euler(0f, 50f, 0f) * Vector3.forward * 5f));
        }

        [Test]
        public void BehindButWithinProximity_Detected() => Assert.IsTrue(Detect(new Vector3(0f, 0f, -1.2f)));

        [Test]
        public void Wall_BlocksEvenProximity()
        {
            Assert.IsFalse(Detect(new Vector3(0f, 0f, 5f), lineOfSight: false));
            Assert.IsFalse(Detect(new Vector3(0f, 0f, -1f), lineOfSight: false));
        }

        [Test]
        public void Tracking_IgnoresCone_RespectsRangeAndWalls()
        {
            Assert.IsTrue(EnemyPerception.CanTrack(Vector3.zero, new Vector3(0f, 0f, -10f), 12f, true));
            Assert.IsFalse(EnemyPerception.CanTrack(Vector3.zero, new Vector3(0f, 0f, -13f), 12f, true));
            Assert.IsFalse(EnemyPerception.CanTrack(Vector3.zero, new Vector3(0f, 0f, 3f), 12f, false));
        }
    }
}
