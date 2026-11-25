using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using WildernessExtraction.Map01;

namespace WildernessExtraction.MapBuilder.EditorTools
{
    /// <summary>一键检查：每条问题带一个物体，窗口里点一下就跳过去。</summary>
    public static class MapBuilderChecks
    {
        public readonly struct Issue
        {
            public readonly GameObject Target;
            public readonly string Message;

            public Issue(GameObject target, string message)
            {
                Target = target;
                Message = message;
            }
        }

        public static List<Issue> Run(MapAuthoringRoot root)
        {
            var issues = new List<Issue>();
            if (root == null)
            {
                return issues;
            }

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                {
                    issues.Add(new Issue(t.gameObject, "缺失脚本：" + t.name));
                }
            }

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    issues.Add(new Issue(filter.gameObject, "缺失模型：" + filter.name));
                }
            }

            var props = MapBuilderPlacement.PlacedProps(root);
            var bounds = props.Select(MapBuilderPlacement.WorldBounds).ToList();
            for (var i = 0; i < props.Count; i++)
            {
                if (bounds[i].size.y > 0.3f && props[i].GetComponentsInChildren<Collider>(true).Length == 0)
                {
                    issues.Add(new Issue(props[i], "没有碰撞（玩家会穿过去）：" + props[i].name));
                }

                for (var j = i + 1; j < props.Count; j++)
                {
                    if (props[i].name == props[j].name
                        && Vector3.Distance(props[i].transform.position, props[j].transform.position) < 0.05f
                        && Quaternion.Angle(props[i].transform.rotation, props[j].transform.rotation) < 1f)
                    {
                        issues.Add(new Issue(props[j], "重复摆放（同一位置有两个）：" + props[j].name));
                    }
                }

                var half = root.worldSize * 0.5f;
                var local = root.transform.InverseTransformPoint(bounds[i].center);
                if (Mathf.Abs(local.x) > half.x || Mathf.Abs(local.z) > half.y)
                {
                    issues.Add(new Issue(props[i], "超出地图范围：" + props[i].name));
                }
            }

            var points = MapBuilderPoints.All(root);
            if (!points.Any(p => p.Type == SpawnPointType.PlayerSpawn))
            {
                issues.Add(new Issue(root.gameObject, "还没有出生点"));
            }
            else if (points.Count(p => p.Type == SpawnPointType.PlayerSpawn) > 1)
            {
                foreach (var extra in points.Where(p => p.Type == SpawnPointType.PlayerSpawn).Skip(1))
                {
                    issues.Add(new Issue(extra.gameObject, "出生点多于一个：" + extra.PointId));
                }
            }

            if (!points.Any(p => p.Type == SpawnPointType.ExtractionCandidate))
            {
                issues.Add(new Issue(root.gameObject, "还没有撤离点"));
            }

            foreach (var duplicate in points.GroupBy(p => p.PointId).Where(g => g.Count() > 1))
            {
                foreach (var point in duplicate.Skip(1))
                {
                    issues.Add(new Issue(point.gameObject, "点位 ID 重复：" + point.PointId));
                }
            }

            var hasNavMesh = NavMesh.CalculateTriangulation().vertices.Length > 0;
            if (hasNavMesh)
            {
                foreach (var point in points)
                {
                    if (!NavMesh.SamplePosition(point.transform.position, out _, 2f, NavMesh.AllAreas))
                    {
                        issues.Add(new Issue(point.gameObject, "点位不在可走区域：" + point.PointId));
                    }
                }
            }

            return issues;
        }
    }
}
