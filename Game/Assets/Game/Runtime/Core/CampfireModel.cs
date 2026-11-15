using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>火堆燃烧计时：点燃 → 临界预警 → 燃尽。燃尽后不可复燃（本版不能添柴）。</summary>
    public sealed class CampfireModel
    {
        public CampfireModel(float burnDuration, float warningDuration)
        {
            BurnDuration = Mathf.Max(0.01f, burnDuration);
            WarningDuration = Mathf.Clamp(warningDuration, 0f, BurnDuration);
            Remaining = BurnDuration;
        }

        public float BurnDuration { get; }
        public float WarningDuration { get; }
        public float Remaining { get; private set; }
        public bool IsBurning => Remaining > 0f;
        public bool IsWarning => IsBurning && Remaining <= WarningDuration;
        public float Normalized => Remaining / BurnDuration;

        /// <summary>推进燃烧；燃尽的那一次调用返回 true。</summary>
        public bool Tick(float deltaTime)
        {
            if (!IsBurning || deltaTime <= 0f)
            {
                return false;
            }

            Remaining = Mathf.Max(0f, Remaining - deltaTime);
            return !IsBurning;
        }
    }

    public enum CampfirePlacementResult
    {
        Valid,
        NoFuel,
        TooFar,
        NotOnGround,
        Blocked,
        OverlapsCampfire,
        InSafeHouseCore,
    }

    /// <summary>火堆放置的纯规则判定。物理与 NavMesh 查询结果由调用方传入。</summary>
    public static class CampfirePlacementRules
    {
        public static CampfirePlacementResult Evaluate(
            Vector3 player, Vector3 site, int fuelCount, int fuelCost, float placeRange, float footprintRadius,
            bool onGround, bool blocked,
            System.Collections.Generic.IEnumerable<Vector3> otherCampfires,
            System.Collections.Generic.IEnumerable<Vector3> safeHouseCores, float safeHouseCoreRadius)
        {
            if (fuelCount < fuelCost) return CampfirePlacementResult.NoFuel;
            if (HorizontalDistance(player, site) > placeRange + 0.001f) return CampfirePlacementResult.TooFar;
            // 阻挡体下方通常没有 NavMesh，先报“被挡住”更易理解。
            if (blocked) return CampfirePlacementResult.Blocked;
            if (!onGround) return CampfirePlacementResult.NotOnGround;

            if (otherCampfires != null)
            {
                foreach (var other in otherCampfires)
                {
                    if (HorizontalDistance(other, site) < footprintRadius * 2f) return CampfirePlacementResult.OverlapsCampfire;
                }
            }

            if (safeHouseCores != null)
            {
                foreach (var core in safeHouseCores)
                {
                    if (HorizontalDistance(core, site) < safeHouseCoreRadius) return CampfirePlacementResult.InSafeHouseCore;
                }
            }

            return CampfirePlacementResult.Valid;
        }

        /// <summary>把鼠标指向的点限制在放置半径内（沿玩家到指向点的方向）。</summary>
        public static Vector3 ClampToRange(Vector3 player, Vector3 aim, float placeRange)
        {
            var offset = aim - player;
            offset.y = 0f;
            if (offset.magnitude <= placeRange) return new Vector3(aim.x, player.y, aim.z);
            return player + offset.normalized * placeRange;
        }

        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
