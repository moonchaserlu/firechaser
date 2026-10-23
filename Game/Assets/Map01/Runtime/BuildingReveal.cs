using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 进入建筑后隐藏屋顶。只看本机观察者，只改屋顶 Renderer 的渲染开关，
    /// 不碰碰撞、导航和联网对象，也不停用建筑根节点。
    /// 多个判定体取并集，门口或重叠房间不会反复闪烁；离开后再等待一个短暂缓冲才恢复。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingReveal : MonoBehaviour
    {
        [SerializeField] private string buildingId;
        [SerializeField] private InteriorVolume[] interiorVolumes = new InteriorVolume[0];
        [Tooltip("RoofRevealGroup 下的屋顶及上层外壳 Renderer。")]
        [SerializeField] private Renderer[] revealRenderers = new Renderer[0];
        [SerializeField, Min(0)] private int floorIndex;
        [Tooltip("离开所有判定体后等待该秒数再恢复屋顶，吸收门口抖动。")]
        [SerializeField, Min(0f)] private float exitGrace = 0.1f;

        private float outsideTime;

        public string BuildingId => buildingId;
        public bool RoofVisible { get; private set; } = true;
        public InteriorVolume[] InteriorVolumes => interiorVolumes;
        public Renderer[] RevealRenderers => revealRenderers;

        public void Configure(string id, InteriorVolume[] volumes, Renderer[] renderers)
        {
            buildingId = id;
            interiorVolumes = volumes;
            revealRenderers = renderers;
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

        private void OnDisable()
        {
            SetRoofVisible(true);
        }

        private void LateUpdate()
        {
            var viewer = RevealViewer.Local;
            var inside = viewer != null && Contains(viewer.transform.position);
            if (inside)
            {
                outsideTime = 0f;
                SetRoofVisible(false);
                return;
            }

            if (RoofVisible)
            {
                return;
            }

            outsideTime += Time.deltaTime;
            if (outsideTime >= exitGrace)
            {
                SetRoofVisible(true);
            }
        }

        private void SetRoofVisible(bool visible)
        {
            if (RoofVisible == visible)
            {
                return;
            }

            RoofVisible = visible;
            // 原型阶段直接切换渲染；阶段 2 换成 0.15–0.30 秒抖动溶解。
            foreach (var roof in revealRenderers)
            {
                if (roof != null)
                {
                    roof.forceRenderingOff = !visible;
                }
            }
        }
    }
}
