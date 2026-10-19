using System;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>XZ 平面移动、鼠标地面瞄准、翻滚与体力。</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        private const float GroundedStickVelocity = -2f;

        [SerializeField] private PlayerConfig config;
        [SerializeField] private PlayerInputReader input;
        [Tooltip("为空时使用 Camera.main。")]
        [SerializeField] private Camera viewCamera;

        private CharacterController controller;
        private StaminaModel stamina;
        private Vector3 dodgeDirection;
        private float dodgeTimeLeft;
        private float verticalVelocity;

        public event Action DodgeStarted;
        public event Action DodgeRejected;

        public StaminaModel Stamina => stamina;
        public bool IsDodging => dodgeTimeLeft > 0f;
        public Vector3 AimDirection { get; private set; } = Vector3.forward;
        public Vector3 AimPoint { get; private set; }

        private Camera ViewCamera => viewCamera != null ? viewCamera : Camera.main;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (input == null)
            {
                input = GetComponent<PlayerInputReader>();
            }

            stamina = new StaminaModel(config.maxStamina, config.staminaRegenPerSecond, config.staminaRegenDelay);
            AimDirection = Flatten(transform.forward, Vector3.forward);
        }

        private void OnDisable()
        {
            dodgeTimeLeft = 0f;
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            stamina.Tick(deltaTime);
            UpdateAim();

            var moveDirection = ToWorldDirection(input.Move);
            if (!IsDodging && input.DodgePressed)
            {
                TryStartDodge(moveDirection);
            }

            Vector3 horizontal;
            if (IsDodging)
            {
                horizontal = dodgeDirection * config.dodgeSpeed;
                dodgeTimeLeft -= deltaTime;
            }
            else
            {
                horizontal = moveDirection * config.moveSpeed;
            }

            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = GroundedStickVelocity;
            }
            else
            {
                verticalVelocity += Physics.gravity.y * deltaTime;
            }

            controller.Move((horizontal + Vector3.up * verticalVelocity) * deltaTime);
            transform.rotation = Quaternion.LookRotation(AimDirection, Vector3.up);
        }

        /// <summary>传送到指定位置（出生点、测试）。</summary>
        public void Teleport(Vector3 position)
        {
            var wasEnabled = controller != null && controller.enabled;
            if (wasEnabled)
            {
                controller.enabled = false;
            }

            transform.position = position;
            verticalVelocity = 0f;
            if (wasEnabled)
            {
                controller.enabled = true;
            }
        }

        private bool TryStartDodge(Vector3 moveDirection)
        {
            if (!stamina.TryConsume(config.dodgeStaminaCost))
            {
                DodgeRejected?.Invoke();
                return false;
            }

            dodgeDirection = moveDirection.sqrMagnitude > 0.0001f ? moveDirection.normalized : AimDirection;
            dodgeTimeLeft = config.dodgeDuration;
            DodgeStarted?.Invoke();
            return true;
        }

        private void UpdateAim()
        {
            var cam = ViewCamera;
            if (cam == null)
            {
                return;
            }

            var ray = cam.ScreenPointToRay(input.AimScreenPosition);
            var ground = new Plane(Vector3.up, transform.position);
            if (!ground.Raycast(ray, out var distance))
            {
                return;
            }

            AimPoint = ray.GetPoint(distance);
            var toAim = AimPoint - transform.position;
            toAim.y = 0f;
            if (toAim.sqrMagnitude > 0.01f)
            {
                AimDirection = toAim.normalized;
            }
        }

        private Vector3 ToWorldDirection(Vector2 moveInput)
        {
            var cam = ViewCamera;
            var forward = cam != null ? Flatten(cam.transform.forward, Vector3.forward) : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            return Vector3.ClampMagnitude(forward * moveInput.y + right * moveInput.x, 1f);
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : fallback;
        }
    }
}
