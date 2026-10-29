using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 把玩家现有的移动、翻滚、射击、换弹事件转成角色模型的 Animator 参数。
    /// 只读取 PlayerMotor / PlayerShooter 的公开状态和事件，不参与任何玩法计算。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAnimationDriver : MonoBehaviour
    {
        private static readonly int MoveXId = Animator.StringToHash("MoveX");
        private static readonly int MoveZId = Animator.StringToHash("MoveZ");
        private static readonly int DodgeId = Animator.StringToHash("Dodge");
        private static readonly int FireId = Animator.StringToHash("Fire");
        private static readonly int ReloadId = Animator.StringToHash("Reload");
        private static readonly int RollSpeedId = Animator.StringToHash("RollSpeed");
        private static readonly int ReloadSpeedId = Animator.StringToHash("ReloadSpeed");

        [SerializeField] private Animator animator;
        [Tooltip("模型根节点；翻滚时转向翻滚方向，结束后转回瞄准方向。")]
        [SerializeField] private Transform model;
        [SerializeField] private PlayerConfig config;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerShooter shooter;
        [Tooltip("动画里翻滚片段的时长（秒），用于按 dodgeDuration 缩放播放速度。")]
        [SerializeField] private float rollClipLength = 0.6f;
        [Tooltip("动画里换弹片段的时长（秒），用于按武器 reloadDuration 缩放播放速度。")]
        [SerializeField] private float reloadClipLength = 1f;
        [SerializeField] private float moveDamping = 0.08f;
        [SerializeField] private int upperBodyLayer = 1;

        private Vector3 lastPosition;
        private Quaternion modelRestRotation;
        private float upperWeight = 1f;

        public void Configure(Animator targetAnimator, Transform targetModel, PlayerConfig playerConfig, float rollLength, float reloadLength)
        {
            animator = targetAnimator;
            model = targetModel;
            config = playerConfig;
            rollClipLength = rollLength;
            reloadClipLength = reloadLength;
        }

        private void Awake()
        {
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (shooter == null) shooter = GetComponent<PlayerShooter>();
            if (model != null) modelRestRotation = model.localRotation;
            lastPosition = transform.position;
        }

        private void OnEnable()
        {
            if (motor != null) motor.DodgeStarted += OnDodgeStarted;
            if (shooter != null)
            {
                shooter.Fired += OnFired;
                shooter.ReloadStarted += OnReloadStarted;
            }
        }

        private void OnDisable()
        {
            if (motor != null) motor.DodgeStarted -= OnDodgeStarted;
            if (shooter != null)
            {
                shooter.Fired -= OnFired;
                shooter.ReloadStarted -= OnReloadStarted;
            }
        }

        private void LateUpdate()
        {
            if (animator == null) return;
            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            var velocity = (transform.position - lastPosition) / deltaTime;
            lastPosition = transform.position;
            velocity.y = 0f;
            var dodging = motor != null && motor.IsDodging;

            // 跑步混合：按角色朝向（=瞄准方向）分解移动速度，并按移动速度归一。
            var speed = config != null && config.moveSpeed > 0f ? config.moveSpeed : 5f;
            var local = transform.InverseTransformDirection(velocity) / speed;
            if (dodging) local = Vector3.zero;
            animator.SetFloat(MoveXId, Mathf.Clamp(local.x, -1f, 1f), moveDamping, deltaTime);
            animator.SetFloat(MoveZId, Mathf.Clamp(local.z, -1f, 1f), moveDamping, deltaTime);

            // 翻滚时模型朝向翻滚方向；上半身持枪层淡出，避免射击姿势盖住翻滚。
            if (model != null)
            {
                var target = modelRestRotation;
                if (dodging && velocity.sqrMagnitude > 0.01f)
                {
                    target = Quaternion.Inverse(transform.rotation) * Quaternion.LookRotation(velocity.normalized, Vector3.up) * modelRestRotation;
                    model.localRotation = target;
                }
                else
                {
                    model.localRotation = Quaternion.Slerp(model.localRotation, target, 1f - Mathf.Exp(-20f * deltaTime));
                }
            }

            upperWeight = Mathf.MoveTowards(upperWeight, dodging ? 0f : 1f, deltaTime / 0.08f);
            if (animator.layerCount > upperBodyLayer) animator.SetLayerWeight(upperBodyLayer, upperWeight);
        }

        private void OnDodgeStarted()
        {
            if (animator == null) return;
            var duration = config != null ? config.dodgeDuration : 0.35f;
            animator.SetFloat(RollSpeedId, duration > 0f ? rollClipLength / duration : 1f);
            animator.ResetTrigger(FireId);
            animator.SetTrigger(DodgeId);
        }

        private void OnFired()
        {
            if (animator != null) animator.SetTrigger(FireId);
        }

        private void OnReloadStarted()
        {
            if (animator == null) return;
            var duration = shooter != null ? shooter.CurrentConfig.reloadDuration : 1.4f;
            animator.SetFloat(ReloadSpeedId, duration > 0f ? reloadClipLength / duration : 1f);
            animator.SetTrigger(ReloadId);
        }
    }
}
