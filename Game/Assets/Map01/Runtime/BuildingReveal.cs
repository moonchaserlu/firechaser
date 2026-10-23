using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 进入建筑后隐藏屋顶。只看本机观察者，只改屋顶 Renderer 的渲染，
    /// 不碰碰撞、导航和联网对象，也不停用建筑根节点。
    /// 多个判定体取并集，门口或重叠房间不会反复闪烁；离开后再等待一个短暂缓冲才恢复。
    /// 有淡出材质时按 fadeDuration 做透明过渡，结束后关闭渲染并换回原材质。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingReveal : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private string buildingId;
        [SerializeField] private InteriorVolume[] interiorVolumes = new InteriorVolume[0];
        [Tooltip("RoofRevealGroup 下的屋顶及上层外壳 Renderer。")]
        [SerializeField] private Renderer[] revealRenderers = new Renderer[0];
        [Tooltip("与 revealRenderers 一一对应的透明淡出材质；为空时直接切换。")]
        [SerializeField] private Material[] fadeMaterials = new Material[0];
        [SerializeField, Min(0)] private int floorIndex;
        [Tooltip("离开所有判定体后等待该秒数再恢复屋顶，吸收门口抖动。")]
        [SerializeField, Min(0f)] private float exitGrace = 0.1f;
        [SerializeField, Range(0f, 1f)] private float fadeDuration = 0.22f;

        private float outsideTime;
        private Material[] originalMaterials;
        private MaterialPropertyBlock block;
        /// <summary>当前屋顶不透明度，0 = 完全隐藏。</summary>
        private float opacity = 1f;

        public string BuildingId => buildingId;
        /// <summary>逻辑状态：本机观察者不在室内时为 true。渲染过渡可能稍有滞后。</summary>
        public bool RoofVisible { get; private set; } = true;
        /// <summary>过渡已结束，渲染与逻辑状态一致。</summary>
        public bool TransitionComplete => Mathf.Approximately(opacity, RoofVisible ? 1f : 0f);
        public float FadeDuration => fadeDuration;
        public InteriorVolume[] InteriorVolumes => interiorVolumes;
        public Renderer[] RevealRenderers => revealRenderers;

        public void Configure(string id, InteriorVolume[] volumes, Renderer[] renderers, Material[] fades = null)
        {
            buildingId = id;
            interiorVolumes = volumes;
            revealRenderers = renderers;
            fadeMaterials = fades ?? new Material[0];
        }

        public bool Contains(Vector3 worldPosition)
        {
            foreach (var volume in interiorVolumes)
            {
                if (volume != null && volume.FloorIndex == floorIndex && volume.Contains(worldPosition))
                {
                    return true;
                }
            }

            return false;
        }

        private void Awake()
        {
            originalMaterials = new Material[revealRenderers.Length];
            for (var i = 0; i < revealRenderers.Length; i++)
            {
                originalMaterials[i] = revealRenderers[i] != null ? revealRenderers[i].sharedMaterial : null;
            }
        }

        private void OnDisable()
        {
            RoofVisible = true;
            ApplyOpacity(1f);
        }

        private void LateUpdate()
        {
            var viewer = RevealViewer.Local;
            var inside = viewer != null && Contains(viewer.transform.position);
            if (inside)
            {
                outsideTime = 0f;
                RoofVisible = false;
            }
            else if (!RoofVisible)
            {
                outsideTime += Time.deltaTime;
                if (outsideTime >= exitGrace)
                {
                    RoofVisible = true;
                }
            }

            var target = RoofVisible ? 1f : 0f;
            if (Mathf.Approximately(opacity, target))
            {
                return;
            }

            var step = fadeDuration > 0f && CanFade ? Time.deltaTime / fadeDuration : 1f;
            ApplyOpacity(Mathf.MoveTowards(opacity, target, step));
        }

        private bool CanFade => fadeMaterials != null && fadeMaterials.Length == revealRenderers.Length;

        private void ApplyOpacity(float value)
        {
            opacity = value;
            var hidden = opacity <= 0f;
            var opaque = opacity >= 1f;
            block ??= new MaterialPropertyBlock();
            for (var i = 0; i < revealRenderers.Length; i++)
            {
                var roof = revealRenderers[i];
                if (roof == null)
                {
                    continue;
                }

                roof.forceRenderingOff = hidden;
                if (!CanFade || originalMaterials == null)
                {
                    continue;
                }

                if (hidden || opaque)
                {
                    roof.sharedMaterial = originalMaterials[i];
                    roof.SetPropertyBlock(null);
                    continue;
                }

                var fade = fadeMaterials[i];
                roof.sharedMaterial = fade;
                var color = fade.GetColor(BaseColorId);
                color.a = opacity;
                block.SetColor(BaseColorId, color);
                roof.SetPropertyBlock(block);
            }
        }
    }
}
