using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 参考《逃离鸭科夫》的遮挡淡出：挡在镜头和玩家之间的树冠变成半透明，玩家附近一圈都看得清。
    /// 只换渲染材质，不碰碰撞和导航。挂在主相机上。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CanopyOcclusionFade : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Transform target;
        [Tooltip("树木所在的分组（正常林与污染林）；只处理其中高于 minHeight 的渲染器。")]
        [SerializeField] private Transform[] treeRoots = new Transform[0];
        [Tooltip("视线周围多宽范围内的树冠会被淡出（米）。")]
        [SerializeField, Min(0f)] private float viewRadius = 3f;
        [SerializeField, Range(0f, 1f)] private float fadedAlpha = 0.25f;
        [SerializeField, Min(0f)] private float minHeight = 2.5f;

        private readonly List<Renderer> candidates = new List<Renderer>();
        private readonly Dictionary<Renderer, Material[]> faded = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Material, Material> fadeMaterials = new Dictionary<Material, Material>();
        private readonly List<Renderer> toRestore = new List<Renderer>();
        private readonly HashSet<Renderer> occluding = new HashSet<Renderer>();

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        public int FadedCount => faded.Count;

        public bool IsFaded(Renderer renderer) => faded.ContainsKey(renderer);

        public void Configure(Transform player, Transform[] roots)
        {
            target = player;
            treeRoots = roots;
            Collect();
        }

        private void Start()
        {
            if (target == null)
            {
                var player = FindAnyObjectByType<PlayerCharacter>();
                if (player != null) target = player.transform;
            }

            Collect();
        }

        private void OnDisable()
        {
            foreach (var pair in faded)
            {
                if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            }

            faded.Clear();
        }

        private void Collect()
        {
            candidates.Clear();
            foreach (var root in treeRoots)
            {
                if (root == null) continue;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is ParticleSystemRenderer || renderer is LineRenderer) continue;
                    if (renderer.bounds.size.y >= minHeight) candidates.Add(renderer);
                }
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            var eye = transform.position;
            var focus = target.position + Vector3.up * 1f;
            var toFocus = focus - eye;
            var distance = toFocus.magnitude;
            var ray = new Ray(eye, toFocus / Mathf.Max(distance, 0.001f));

            occluding.Clear();
            foreach (var renderer in candidates)
            {
                if (renderer == null || !renderer.enabled) continue;
                var bounds = renderer.bounds;
                bounds.Expand(viewRadius * 2f);
                // 只管视线上、且在玩家前面（靠镜头一侧）的树。
                if (bounds.IntersectRay(ray, out var hit) && hit < distance)
                {
                    occluding.Add(renderer);
                }
            }

            foreach (var renderer in occluding)
            {
                if (!faded.ContainsKey(renderer)) Fade(renderer);
            }

            toRestore.Clear();
            foreach (var renderer in faded.Keys)
            {
                if (!occluding.Contains(renderer)) toRestore.Add(renderer);
            }

            foreach (var renderer in toRestore)
            {
                if (renderer != null) renderer.sharedMaterials = faded[renderer];
                faded.Remove(renderer);
            }
        }

        private void Fade(Renderer renderer)
        {
            var original = renderer.sharedMaterials;
            var replaced = new Material[original.Length];
            for (var i = 0; i < original.Length; i++)
            {
                replaced[i] = FadeMaterialFor(original[i]);
            }

            faded[renderer] = original;
            renderer.sharedMaterials = replaced;
        }

        private Material FadeMaterialFor(Material source)
        {
            if (source == null) return null;
            if (fadeMaterials.TryGetValue(source, out var fade)) return fade;

            // URP Lit 透明变体：工程里屋顶淡出材质已经用到，构建时不会被剔除。
            fade = new Material(source) { name = source.name + "_OcclusionFade" };
            fade.SetFloat("_Surface", 1f);
            fade.SetFloat("_Blend", 0f);
            fade.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            fade.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            fade.SetFloat("_ZWrite", 0f);
            fade.SetOverrideTag("RenderType", "Transparent");
            fade.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            fade.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            if (fade.HasProperty(BaseColorId))
            {
                var color = fade.GetColor(BaseColorId);
                color.a = fadedAlpha;
                fade.SetColor(BaseColorId, color);
            }

            fadeMaterials[source] = fade;
            return fade;
        }
    }
}
