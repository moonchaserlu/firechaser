using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 室内判定体（盒形，局部坐标）。不使用 Collider，避免触发体进入物理与导航烘焙，
    /// 也不依赖 CharacterController 传送时的触发事件。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteriorVolume : MonoBehaviour
    {
        [SerializeField] private Vector3 size = new Vector3(4f, 3f, 4f);
        [Tooltip("多层建筑预留：该判定体所在楼层。")]
        [SerializeField, Min(0)] private int floorIndex;

        public Vector3 Size
        {
            get => size;
            set => size = value;
        }

        public int FloorIndex => floorIndex;

        /// <summary>判定体以自身变换为底面中心，向上延伸 size.y。</summary>
        public bool Contains(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition);
            return Mathf.Abs(local.x) <= size.x * 0.5f
                   && Mathf.Abs(local.z) <= size.z * 0.5f
                   && local.y >= -0.5f
                   && local.y <= size.y;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, size.y * 0.5f, 0f), size);
        }
    }
}
