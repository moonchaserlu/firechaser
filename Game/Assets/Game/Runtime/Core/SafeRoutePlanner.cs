using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>光路规划节点：起点、终点或已知有效光源。Radius 为该点到所在光区边缘的距离（不在光内为 0）。</summary>
    public readonly struct RouteNode
    {
        public RouteNode(Vector3 position, float radius)
        {
            Position = position;
            Radius = Mathf.Max(0f, radius);
        }

        public Vector3 Position { get; }
        public float Radius { get; }
    }

    /// <summary>
    /// AI Bot 光路规划（策划案 5.3）：把路线拆成连续安全节点，逐段估算黑暗时长与黑暗伤害。
    /// 每段离光后先有预警期，回到光内即清零，因此逐段独立计算。超过伤害预算的路线（可能致死）被禁止。
    /// </summary>
    public static class SafeRoutePlanner
    {
        public readonly struct Rules
        {
            public Rules(float moveSpeed, float warningDuration, float damagePerSecond, float damageWeight)
            {
                MoveSpeed = Mathf.Max(0.1f, moveSpeed);
                WarningDuration = Mathf.Max(0f, warningDuration);
                DamagePerSecond = Mathf.Max(0f, damagePerSecond);
                DamageWeight = Mathf.Max(0f, damageWeight);
            }

            public float MoveSpeed { get; }
            public float WarningDuration { get; }
            public float DamagePerSecond { get; }
            /// <summary>每点预计伤害折算的路程代价（米），越大越偏好全程有光。</summary>
            public float DamageWeight { get; }
        }

        /// <summary>一段路程中预计的黑暗伤害。</summary>
        public static float SegmentDamage(float pathLength, float radiusA, float radiusB, Rules rules)
        {
            var dark = Mathf.Max(0f, pathLength - radiusA - radiusB);
            var darkSeconds = dark / rules.MoveSpeed;
            return Mathf.Max(0f, darkSeconds - rules.WarningDuration) * rules.DamagePerSecond;
        }

        /// <summary>
        /// 在 start 与 goal 之间寻找代价最低、且总预计伤害不超过 maxDamage 的节点序列（不含 start，含 goal）。
        /// distance 返回两节点间实际路程，不可达时返回 +∞。
        /// </summary>
        public static bool TryPlan(IReadOnlyList<RouteNode> nodes, int start, int goal, Rules rules, float maxDamage,
            Func<int, int, float> distance, out List<int> path, out float totalDamage, out float totalLength)
        {
            path = null;
            totalDamage = 0f;
            totalLength = 0f;
            var count = nodes.Count;
            var cost = new float[count];
            var damage = new float[count];
            var length = new float[count];
            var previous = new int[count];
            var done = new bool[count];
            for (var i = 0; i < count; i++)
            {
                cost[i] = float.PositiveInfinity;
                previous[i] = -1;
            }

            cost[start] = 0f;
            for (var iteration = 0; iteration < count; iteration++)
            {
                var current = -1;
                for (var i = 0; i < count; i++)
                {
                    if (!done[i] && !float.IsPositiveInfinity(cost[i]) && (current < 0 || cost[i] < cost[current])) current = i;
                }

                if (current < 0 || current == goal) break;
                done[current] = true;

                for (var next = 0; next < count; next++)
                {
                    if (done[next] || next == current) continue;
                    var segment = distance(current, next);
                    if (float.IsPositiveInfinity(segment) || float.IsNaN(segment)) continue;
                    var segmentDamage = SegmentDamage(segment, nodes[current].Radius, nodes[next].Radius, rules);
                    var accumulated = damage[current] + segmentDamage;
                    if (accumulated > maxDamage) continue;
                    var candidate = cost[current] + segment + segmentDamage * rules.DamageWeight;
                    if (candidate < cost[next])
                    {
                        cost[next] = candidate;
                        damage[next] = accumulated;
                        length[next] = length[current] + segment;
                        previous[next] = current;
                    }
                }
            }

            if (float.IsPositiveInfinity(cost[goal])) return false;

            path = new List<int>();
            for (var node = goal; node != start; node = previous[node]) path.Add(node);
            path.Reverse();
            totalDamage = damage[goal];
            totalLength = length[goal];
            return true;
        }
    }
}
