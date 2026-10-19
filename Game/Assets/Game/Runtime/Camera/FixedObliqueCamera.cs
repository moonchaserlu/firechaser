using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>固定角度斜俯视相机。在 LateUpdate 中直接对齐目标，不做插值，避免抖动。</summary>
    [DisallowMultipleComponent]
    public sealed class FixedObliqueCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 16f, -11f);
        [SerializeField] private Vector3 eulerAngles = new Vector3(55f, 0f, 0f);

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            transform.SetPositionAndRotation(target.position + offset, Quaternion.Euler(eulerAngles));
        }
    }
}
