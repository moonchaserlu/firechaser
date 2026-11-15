using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace WildernessExtraction.Tests
{
    public sealed class SafeRoutePlannerTests
    {
        // 速度 5 m/s，预警 1.5 秒，每秒 20 伤害，每点伤害折算 4 米。
        private static readonly SafeRoutePlanner.Rules Rules = new SafeRoutePlanner.Rules(5f, 1.5f, 20f, 4f);

        private static float Straight(IReadOnlyList<RouteNode> nodes, int a, int b) => Vector3.Distance(nodes[a].Position, nodes[b].Position);

        [Test]
        public void SegmentDamage_ShortGapWithinWarning_IsZero()
        {
            // 两盏半径 4 的灯相距 14 米：黑暗 6 米 = 1.2 秒 < 预警。
            Assert.AreEqual(0f, SafeRoutePlanner.SegmentDamage(14f, 4f, 4f, Rules));
            // 黑暗 15 米 = 3 秒：超出预警 1.5 秒 × 20 = 30。
            Assert.AreEqual(30f, SafeRoutePlanner.SegmentDamage(23f, 4f, 4f, Rules), 1e-3f);
        }

        [Test]
        public void LethalDarkRoute_IsForbidden()
        {
            // 起点在光内边缘，终点在 60 米外全黑：预计伤害远超预算。
            var nodes = new List<RouteNode> { new RouteNode(Vector3.zero, 0f), new RouteNode(new Vector3(0f, 0f, 60f), 0f) };
            Assert.IsFalse(SafeRoutePlanner.TryPlan(nodes, 0, 1, Rules, 70f, (a, b) => Straight(nodes, a, b), out _, out _, out _),
                "目的地有光但途中必死的路线不能被选中");
        }

        [Test]
        public void PrefersContinuousLights_OverShorterDarkCut()
        {
            // 直线 30 米全黑（伤害 > 0），绕经两盏灯的路线更长但几乎无伤。
            var nodes = new List<RouteNode>
            {
                new RouteNode(Vector3.zero, 0f),
                new RouteNode(new Vector3(0f, 0f, 30f), 0f),
                new RouteNode(new Vector3(-6f, 0f, 9f), 6f),
                new RouteNode(new Vector3(-6f, 0f, 21f), 6f),
            };

            Assert.IsTrue(SafeRoutePlanner.TryPlan(nodes, 0, 1, Rules, 70f, (a, b) => Straight(nodes, a, b), out var path, out var damage, out _));
            CollectionAssert.AreEqual(new[] { 2, 3, 1 }, path);
            Assert.AreEqual(0f, damage, 1e-3f);
        }

        [Test]
        public void UnreachableSegments_AreSkipped()
        {
            var nodes = new List<RouteNode> { new RouteNode(Vector3.zero, 0f), new RouteNode(new Vector3(0f, 0f, 5f), 0f) };
            Assert.IsFalse(SafeRoutePlanner.TryPlan(nodes, 0, 1, Rules, 100f, (a, b) => float.PositiveInfinity, out _, out _, out _));
        }
    }

    public sealed class AiBotRulesTests
    {
        [Test]
        public void DamageFalloff_MatchesPlayerRule()
        {
            Assert.AreEqual(1f, AiBotRules.DamageFalloff(10f, 18f, 25f, 0.7f));
            Assert.AreEqual(0.7f, AiBotRules.DamageFalloff(25f, 18f, 25f, 0.7f), 1e-4f);
            Assert.AreEqual(0.85f, AiBotRules.DamageFalloff(21.5f, 18f, 25f, 0.7f), 1e-4f);
        }

        [Test]
        public void BotSpread_IsWorseThanPlayer()
        {
            Assert.AreEqual(3.6f, AiBotRules.Spread(2f, 4f, false, 1.8f), 1e-4f);
            Assert.AreEqual(7.2f, AiBotRules.Spread(2f, 4f, true, 1.8f), 1e-4f);
            Assert.AreEqual(2f, AiBotRules.Spread(2f, 4f, false, 0.5f), 1e-4f, "倍率不低于 1：人机不会比玩家更准");
        }
    }
}
