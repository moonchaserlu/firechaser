using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WildernessExtraction.MapBuilder.EditorTools
{
    /// <summary>
    /// 放置规则：贴地 / 贴墙、网格吸附、和旁边已摆物体边对边对齐、重叠判定、实际实例化。
    /// 不依赖窗口状态，测试直接调用。
    /// </summary>
    public static class MapBuilderPlacement
    {
        /// <summary>边对边吸附的最大距离（米）。</summary>
        public const float EdgeSnapDistance = 0.6f;

        /// <summary>判定重叠时两边各收缩的余量（米），贴边摆放不算重叠。</summary>
        public const float OverlapTolerance = 0.06f;

        public struct Request
        {
            public GameObject asset;
            public MapBuilderLibrary.Group group;
            public Vector3 position;
            public Quaternion rotation;
            public float scaleMultiplier;
            public bool addColliders;
        }

        public static Vector3 SnapToGrid(Vector3 position, Transform root, float grid)
        {
            if (grid <= 0f)
            {
                return position;
            }

            var local = root != null ? root.InverseTransformPoint(position) : position;
            local.x = Mathf.Round(local.x / grid) * grid;
            local.z = Mathf.Round(local.z / grid) * grid;
            return root != null ? root.TransformPoint(local) : local;
        }

        /// <summary>
        /// 让 ghost 的某条边贴上附近物体的边（相邻或对齐）。只看在另一轴上与 ghost 有交叠或相距不远的物体。
        /// 返回需要平移的 XZ 偏移。
        /// </summary>
        public static Vector3 EdgeSnapOffset(Bounds ghost, IEnumerable<Bounds> others, float threshold = EdgeSnapDistance)
        {
            float bestX = float.MaxValue, bestZ = float.MaxValue;
            foreach (var other in others)
            {
                if (other.size.x <= 0f && other.size.z <= 0f)
                {
                    continue;
                }

                if (RangesNear(ghost.min.z, ghost.max.z, other.min.z, other.max.z, threshold))
                {
                    Consider(ref bestX, other.max.x - ghost.min.x, threshold);
                    Consider(ref bestX, other.min.x - ghost.max.x, threshold);
                    Consider(ref bestX, other.min.x - ghost.min.x, threshold);
                    Consider(ref bestX, other.max.x - ghost.max.x, threshold);
                }

                if (RangesNear(ghost.min.x, ghost.max.x, other.min.x, other.max.x, threshold))
                {
                    Consider(ref bestZ, other.max.z - ghost.min.z, threshold);
                    Consider(ref bestZ, other.min.z - ghost.max.z, threshold);
                    Consider(ref bestZ, other.min.z - ghost.min.z, threshold);
                    Consider(ref bestZ, other.max.z - ghost.max.z, threshold);
                }
            }

            return new Vector3(bestX == float.MaxValue ? 0f : bestX, 0f, bestZ == float.MaxValue ? 0f : bestZ);
        }

        private static void Consider(ref float best, float delta, float threshold)
        {
            if (Mathf.Abs(delta) <= threshold && Mathf.Abs(delta) < Mathf.Abs(best))
            {
                best = delta;
            }
        }

        private static bool RangesNear(float aMin, float aMax, float bMin, float bMax, float gap)
        {
            return aMin <= bMax + gap && bMin <= aMax + gap;
        }

        /// <summary>两边各收缩 <see cref="OverlapTolerance"/> 后仍相交才算重叠，贴边、叠放不算。</summary>
        public static bool Overlaps(Bounds a, Bounds b)
        {
            var shrink = Vector3.one * OverlapTolerance * 2f;
            var sa = new Bounds(a.center, Vector3.Max(Vector3.zero, a.size - shrink));
            var sb = new Bounds(b.center, Vector3.Max(Vector3.zero, b.size - shrink));
            return sa.size.x > 0f && sa.size.z > 0f && sb.size.x > 0f && sb.size.z > 0f && sa.Intersects(sb);
        }

        public static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(go.transform.position, Vector3.zero);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        /// <summary>已摆素材（“素材”分组下的直接子物体，分组里再按素材组分一层）。</summary>
        public static List<GameObject> PlacedProps(MapAuthoringRoot root)
        {
            var list = new List<GameObject>();
            if (root == null)
            {
                return list;
            }

            var props = root.transform.Find(MapAuthoringRoot.PropsGroup);
            if (props == null)
            {
                return list;
            }

            foreach (Transform category in props)
            {
                foreach (Transform child in category)
                {
                    list.Add(child.gameObject);
                }
            }

            return list;
        }

        public static GameObject Place(MapAuthoringRoot root, Request request)
        {
            var scene = root.gameObject.scene;
            var instance = PrefabUtility.InstantiatePrefab(request.asset, scene) as GameObject;
            if (instance == null)
            {
                instance = Object.Instantiate(request.asset);
                instance.name = request.asset.name;
            }

            var categoryName = request.group != null && !string.IsNullOrEmpty(request.group.name) ? request.group.name : "其他";
            var category = root.Group(MapAuthoringRoot.PropsGroup).Find(categoryName);
            if (category == null)
            {
                var go = new GameObject(categoryName);
                Undo.RegisterCreatedObjectUndo(go, "地图搭建器：新建分组");
                go.transform.SetParent(root.Group(MapAuthoringRoot.PropsGroup), false);
                category = go.transform;
            }

            instance.transform.SetParent(category, true);
            instance.transform.SetPositionAndRotation(request.position, request.rotation);
            instance.transform.localScale = request.asset.transform.localScale * Mathf.Max(0.01f, request.scaleMultiplier);
            if (request.group != null && request.group.materialOverride != null)
            {
                ApplyMaterial(instance, request.group.materialOverride);
            }

            if (request.addColliders)
            {
                AddMeshColliders(instance);
            }

            Undo.RegisterCreatedObjectUndo(instance, "地图搭建器：放置 " + request.asset.name);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            return instance;
        }

        public static void ApplyMaterial(GameObject instance, Material material)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }

                renderer.sharedMaterials = materials.Length > 0 ? materials : new[] { material };
            }
        }

        /// <summary>没有碰撞体的模型按网格补 MeshCollider，方便后续贴墙、叠放和烘焙导航。</summary>
        public static void AddMeshColliders(GameObject instance)
        {
            if (instance.GetComponentsInChildren<Collider>(true).Length > 0)
            {
                return;
            }

            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null)
                {
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                }
            }
        }
    }
}
