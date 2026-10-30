using UnityEngine;

namespace WildernessExtraction.Shelter
{
    /// <summary>
    /// 安全屋里一间有灯的房间：整个房间（盒形，局部坐标，底面中心为原点）算有效光。
    /// 安全屋同样遵守黑暗规则，只有亮着的房间安全；第二阶段由设施等级决定哪些房间亮。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShelterRoomLight : MonoBehaviour, ISafeLightArea
    {
        [SerializeField] private Vector3 size = new Vector3(8f, 3.5f, 8f);
        [SerializeField] private bool lit = true;
        [SerializeField] private Light[] visuals = new Light[0];

        public bool IsLit => lit && isActiveAndEnabled;
        public Vector3 Size => size;

        public void Configure(Vector3 roomSize, bool isLit, Light[] lights)
        {
            size = roomSize;
            lit = isLit;
            visuals = lights;
            ApplyVisuals();
        }

        public void SetLit(bool value)
        {
            lit = value;
            ApplyVisuals();
        }

        public bool Contains(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f && local.y >= -0.5f && local.y <= size.y;
        }

        private void OnEnable()
        {
            LightRegistry.Scene.Register(this);
            ApplyVisuals();
        }

        private void OnDisable() => LightRegistry.Scene.Unregister(this);

        private void ApplyVisuals()
        {
            foreach (var light in visuals)
            {
                if (light != null) light.enabled = lit;
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.35f, 0.5f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, size.y * 0.5f, 0f), size);
        }
    }
}
