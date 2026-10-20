using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>怪物视觉判定的纯规则。视线遮挡由调用方用物理查询得出后传入。</summary>
    public static class EnemyPerception
    {
        /// <summary>
        /// 未警戒时能否看见目标：无遮挡，且（处于近身半径内，或处于视野距离与正面视锥内）。
        /// </summary>
        public static bool CanDetect(Vector3 position, Vector3 forward, Vector3 targetPosition,
            float sightRange, float fieldOfView, float proximityRadius, bool hasLineOfSight)
        {
            if (!hasLineOfSight) return false;
            var toTarget = Flatten(targetPosition - position);
            var distance = toTarget.magnitude;
            if (distance <= proximityRadius) return true;
            if (distance > sightRange) return false;
            if (fieldOfView >= 360f || distance < 0.0001f) return true;
            return Vector3.Angle(Flatten(forward), toTarget) <= fieldOfView * 0.5f;
        }

        /// <summary>追击中能否继续看见目标：无遮挡且在追击视认距离内（不受视锥限制）。</summary>
        public static bool CanTrack(Vector3 position, Vector3 targetPosition, float chaseSightRange, bool hasLineOfSight)
        {
            return hasLineOfSight && Flatten(targetPosition - position).magnitude <= chaseSightRange;
        }

        private static Vector3 Flatten(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }
    }
}
