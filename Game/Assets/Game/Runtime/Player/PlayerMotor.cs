using System;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>XZ 平面移动、鼠标地面瞄准、奔跑、翻滚与体力；精确瞄准、收枪、使用物品时调整移速。</summary>
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
        private bool sprintLocked;
        private PlayerShooter shooter;
        private PlayerItemUser itemUser;

        public event Action DodgeStarted;
        public event Action DodgeRejected;

        public StaminaModel Stamina => stamina;
        public bool IsDodging => dodgeTimeLeft > 0f;
        /// <summary>本帧正在奔跑（按住 Shift、在移动、未瞄准/开火/用物品且体力足够）。</summary>
        public bool IsSprinting { get; private set; }
        /// <summary>按住右键精确瞄准：减速、降低散布、镜头前移更远。</summary>
        public bool IsAiming => input != null && input.AimDownSightsHeld && !IsDodging;
        public Vector3 AimDirection { get; private set; } = Vector3.forward;
        public Vector3 AimPoint { get; private set; }

        private Camera ViewCamera => viewCamera != null ? viewCamera : Camera.main;
        private PlayerShooter Shooter => shooter != null ? shooter : shooter = GetComponent<PlayerShooter>();
        // PlayerItemUser 可能由 PlayerCharacter 在运行时补挂，惰性获取。
        private PlayerItemUser ItemUser => itemUser != null ? itemUser : itemUser = GetComponent<PlayerItemUser>();
        private bool UsingItem => ItemUser != null && ItemUser.IsUsing;

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
            IsSprinting = false;
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

            UpdateSprint(moveDirection, deltaTime);

            Vector3 horizontal;
            if (IsDodging)
            {
                horizontal = dodgeDirection * config.dodgeSpeed;
                dodgeTimeLeft -= deltaTime;
            }
            else
            {
                horizontal = moveDirection * CurrentMoveSpeed();
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
            // 奔跑时面朝移动方向（奔跑中不能开火），其余时候面朝准星。
            var facing = IsSprinting ? Flatten(moveDirection, AimDirection) : AimDirection;
            transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
        }

        private void UpdateSprint(Vector3 moveDirection, float deltaTime)
        {
            if (!input.SprintHeld)
            {
                sprintLocked = false;
            }

            var wantsSprint = input.SprintHeld && moveDirection.sqrMagnitude > 0.01f && !IsDodging && !IsAiming && !input.AttackHeld && !UsingItem;
            IsSprinting = false;
            if (!wantsSprint || sprintLocked)
            {
                return;
            }

            if (stamina.TryConsume(config.sprintStaminaPerSecond * deltaTime))
            {
                IsSprinting = true;
            }
            else
            {
                // 体力耗尽：变回走路，松开 Shift 后才能再次奔跑。
                sprintLocked = true;
            }
        }

        private float CurrentMoveSpeed()
        {
            var speed = IsSprinting ? config.sprintSpeed : config.moveSpeed;
            if (IsAiming) speed *= config.aimMoveMultiplier;
            if (Shooter != null && Shooter.IsHolstered) speed *= config.holsteredMoveMultiplier;
            if (UsingItem) speed *= config.itemUseMoveMultiplier;
            return speed;
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
