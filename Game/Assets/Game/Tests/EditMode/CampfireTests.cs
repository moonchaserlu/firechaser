using NUnit.Framework;
using UnityEngine;

namespace WildernessExtraction.Tests
{
    public sealed class CampfireModelTests
    {
        [Test]
        public void Lit_BurnsForDuration_WarnsInFinalSeconds_ThenBurnsOut()
        {
            var model = new CampfireModel(90f, 15f);
            Assert.IsTrue(model.IsBurning);
            Assert.IsFalse(model.IsWarning);

            Assert.IsFalse(model.Tick(74f));
            Assert.IsFalse(model.IsWarning);
            Assert.IsFalse(model.Tick(2f));
            Assert.IsTrue(model.IsWarning);

            Assert.IsTrue(model.Tick(20f), "燃尽的那一帧返回 true");
            Assert.IsFalse(model.IsBurning);
            Assert.IsFalse(model.IsWarning);
            Assert.IsFalse(model.Tick(1f), "燃尽后不再重复触发");
        }
    }

    public sealed class CampfirePlacementRulesTests
    {
        private static CampfirePlacementResult Evaluate(Vector3 site, int fuel = 1, bool onGround = true, bool blocked = false,
            Vector3[] fires = null, Vector3[] cores = null)
        {
            return CampfirePlacementRules.Evaluate(Vector3.zero, site, fuel, 1, 3f, 0.6f, onGround, blocked,
                fires ?? new Vector3[0], cores ?? new Vector3[0], 3f);
        }

        [Test]
        public void ValidSite() => Assert.AreEqual(CampfirePlacementResult.Valid, Evaluate(new Vector3(2f, 0f, 0f)));

        [Test]
        public void NoFuel_Rejected() => Assert.AreEqual(CampfirePlacementResult.NoFuel, Evaluate(new Vector3(2f, 0f, 0f), fuel: 0));

        [Test]
        public void BeyondThreeMeters_Rejected() => Assert.AreEqual(CampfirePlacementResult.TooFar, Evaluate(new Vector3(3.2f, 0f, 0f)));

        [Test]
        public void OffGround_Rejected() => Assert.AreEqual(CampfirePlacementResult.NotOnGround, Evaluate(Vector3.right, onGround: false));

        [Test]
        public void Obstacle_Rejected() => Assert.AreEqual(CampfirePlacementResult.Blocked, Evaluate(Vector3.right, blocked: true));

        [Test]
        public void OverlappingAnotherCampfire_Rejected()
        {
            Assert.AreEqual(CampfirePlacementResult.OverlapsCampfire, Evaluate(Vector3.right, fires: new[] { new Vector3(2f, 0f, 0f) }));
            Assert.AreEqual(CampfirePlacementResult.Valid, Evaluate(Vector3.right, fires: new[] { new Vector3(-1f, 0f, 0f) }));
        }

        [Test]
        public void InsideSafeHouseCore_Rejected()
        {
            Assert.AreEqual(CampfirePlacementResult.InSafeHouseCore, Evaluate(Vector3.right, cores: new[] { new Vector3(-1f, 0f, 0f) }));
        }

        [Test]
        public void ClampToRange_KeepsSiteWithinPlaceRange()
        {
            var site = CampfirePlacementRules.ClampToRange(Vector3.zero, new Vector3(10f, 0f, 0f), 3f);
            Assert.AreEqual(3f, CampfirePlacementRules.HorizontalDistance(Vector3.zero, site), 1e-4f);
            Assert.AreEqual(CampfirePlacementResult.Valid, Evaluate(site));
        }
    }

    public sealed class GuaranteedLootTests
    {
        [Test]
        public void GuaranteedEntries_AlwaysDrop_InAdditionToRandomRolls()
        {
            var fuel = ScriptableObject.CreateInstance<ItemDefinition>();
            var junk = ScriptableObject.CreateInstance<ItemDefinition>();
            var table = ScriptableObject.CreateInstance<LootTable>();
            try
            {
                table.randomRolls = 2;
                table.entries.Add(new LootTable.Entry(junk, 0) { weight = 1f, minCount = 1, maxCount = 1 });
                table.guaranteedEntries.Add(new LootTable.Entry(fuel, 2));

                for (var seed = 0; seed < 20; seed++)
                {
                    var drops = table.CreateDrops(new System.Random(seed));
                    Assert.AreEqual(2, drops.FindAll(item => item == fuel).Count);
                    Assert.AreEqual(4, drops.Count);
                }
            }
            finally
            {
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(fuel);
                Object.DestroyImmediate(junk);
            }
        }
    }
}
