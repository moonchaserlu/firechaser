using UnityEngine;
using UnityEngine.InputSystem;

namespace WildernessExtraction
{
    /// <summary>
    /// 固定角度斜俯视相机（长焦：约 42m、FOV 24）。位置在 LateUpdate 中直接对齐目标，不做插值，避免抖动；
    /// 只有朝鼠标方向的前移偏移量做平滑。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FixedObliqueCamera : MonoBehaviour
    {
        /// <summary>42m 距离、55° 俯角对应的偏移。场景构建脚本统一用这组值创建相机。</summary>
        public static readonly Vector3 DefaultOffset = new Vector3(0f, 34.4f, -24.1f);
        public static readonly Vector3 DefaultEulerAngles = new Vector3(55f, 0f, 0f);
        public const float DefaultFieldOfView = 24f;
        public const float DefaultNearClip = 2f;
        public const float DefaultFollowHeight = 0.5f;

        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 34.4f, -24.1f);
        [SerializeField] private Vector3 eulerAngles = new Vector3(55f, 0f, 0f);
        [Tooltip("跟随点相对目标脚底的高度。")]
        [SerializeField] private float followHeight = DefaultFollowHeight;

        [Header("鼠标前移")]
        [SerializeField] private bool lookAheadEnabled = true;
        [Tooltip("鼠标离屏幕中心的地面距离乘以该系数，得到镜头前移量。")]
        [SerializeField, Range(0f, 1f)] private float lookAheadFactor = 0.5f;
        [SerializeField, Min(0f)] private float maxLookAhead = 3f;
        [SerializeField, Min(0f)] private float lookAheadSpeed = 8f;
        [Tooltip("按住攻击时的平滑速度，更低让射击时镜头更稳。")]
        [SerializeField, Min(0f)] private float attackLookAheadSpeed = 2f;
        [Tooltip("为空时从目标上查找。")]
        [SerializeField] private PlayerInputReader input;

        private Camera viewCamera;
        private Vector3 lookAhead;

        public Transform Target
        {
            get => target;
            set
            {
                target = value;
                input = null;
                lookAhead = Vector3.zero;
            }
        }

        public bool LookAheadEnabled
        {
            get => lookAheadEnabled;
            set => lookAheadEnabled = value;
        }

        public Vector3 LookAhead => lookAhead;

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            UpdateLookAhead();
            var focus = target.position + Vector3.up * followHeight + lookAhead;
            transform.SetPositionAndRotation(focus + offset, Quaternion.Euler(eulerAngles));
        }

        private void UpdateLookAhead()
        {
            if (input == null)
            {
                input = target.GetComponentInParent<PlayerInputReader>();
            }

            var desired = Vector3.zero;
            if (lookAheadEnabled && input != null && input.GameplayEnabled && Pointer.current != null && Application.isFocused)
            {
                desired = DesiredLookAhead(input.AimScreenPosition);
            }

            var speed = input != null && input.AttackHeld ? attackLookAheadSpeed : lookAheadSpeed;
            lookAhead = Vector3.Lerp(lookAhead, desired, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
        }

        /// <summary>鼠标点与屏幕中心点在跟随平面上的差，与相机当前平移无关，不会自我放大。</summary>
        private Vector3 DesiredLookAhead(Vector2 screenPosition)
        {
            if (viewCamera == null && !TryGetComponent(out viewCamera))
            {
                return Vector3.zero;
            }

            var plane = new Plane(Vector3.up, target.position + Vector3.up * followHeight);
            screenPosition.x = Mathf.Clamp(screenPosition.x, 0f, Screen.width);
            screenPosition.y = Mathf.Clamp(screenPosition.y, 0f, Screen.height);
            if (!TryProject(plane, screenPosition, out var pointer) ||
                !TryProject(plane, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out var center))
            {
                return Vector3.zero;
            }

            var delta = pointer - center;
            delta.y = 0f;
            return Vector3.ClampMagnitude(delta * lookAheadFactor, maxLookAhead);
        }

        private bool TryProject(Plane plane, Vector2 screenPosition, out Vector3 point)
        {
            var ray = viewCamera.ScreenPointToRay(screenPosition);
            if (plane.Raycast(ray, out var distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }

            point = default;
            return false;
        }
    }
}
