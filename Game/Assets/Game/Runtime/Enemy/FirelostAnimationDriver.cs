using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 把近战怪物（MeleeEnemy）的移动、攻击、受伤、死亡转成失火者模型的 Animator 参数。
    /// 只订阅 MeleeEnemy / Health 的公开事件、读取位置变化，不参与 AI、伤害和导航。
    /// 攻击：MeleeEnemy 进入 Attacking 后 attackWindup 秒出伤害，之后每 attackInterval 秒一次；
    /// 爪击片段的命中帧在 impactTime 秒，所以按下一次出伤害的时间提前 impactTime 起手。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FirelostAnimationDriver : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int AttackRId = Animator.StringToHash("AttackR");
        private static readonly int AttackLId = Animator.StringToHash("AttackL");
        private static readonly int HitId = Animator.StringToHash("Hit");
        private static readonly int DieId = Animator.StringToHash("Die");
        private static readonly int AttackTag = Animator.StringToHash("Attack");

        [SerializeField] private Animator animator;
        [Tooltip("模型根节点；死亡时抵消 MeleeEnemy 灰盒倒地的整体旋转，改由死亡动画倒地。")]
        [SerializeField] private Transform model;
        [SerializeField] private EnemyConfig config;
        [SerializeField] private MeleeEnemy enemy;
        [Tooltip("爪击片段从起手到命中帧的时长（秒）。")]
        [SerializeField] private float impactTime = 0.367f;
        [SerializeField] private float speedDamping = 0.1f;
        [Tooltip("MeleeEnemy 死亡时把根节点抬高的距离，用于把模型放回地面。")]
        [SerializeField] private float deathLift = 0.4f;

        private Health health;
        private Vector3 lastPosition;
        private float nextSwingTime = float.PositiveInfinity;
        private bool rightNext = true;
        private bool dead;
        private float deadYaw;
        private Vector3 deadGround;
        private Quaternion modelRestRotation;

        public float NextSwingTime => nextSwingTime;

        public void Configure(Animator targetAnimator, Transform targetModel, EnemyConfig enemyConfig, float attackImpactTime)
        {
            animator = targetAnimator;
            model = targetModel;
            config = enemyConfig;
            impactTime = attackImpactTime;
        }

        private void Awake()
        {
            if (enemy == null) enemy = GetComponent<MeleeEnemy>();
            health = GetComponent<Health>();
            if (model != null) modelRestRotation = model.localRotation;
            lastPosition = transform.position;
        }

        private void OnEnable()
        {
            if (enemy != null)
            {
                enemy.StateChanged += OnStateChanged;
                enemy.Attacked += OnAttacked;
            }

            if (health != null) health.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            if (enemy != null)
            {
                enemy.StateChanged -= OnStateChanged;
                enemy.Attacked -= OnAttacked;
            }

            if (health != null) health.Damaged -= OnDamaged;
        }

        private void LateUpdate()
        {
            if (animator == null) return;
            if (dead)
            {
                // MeleeEnemy 死亡时把根节点绕 X 转 90° 并抬高；模型保持直立贴地，由 Death 动画自己倒下。
                model.SetPositionAndRotation(deadGround, Quaternion.Euler(0f, deadYaw, 0f) * modelRestRotation);
                return;
            }

            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;
            var velocity = (transform.position - lastPosition) / deltaTime;
            velocity.y = 0f;
            lastPosition = transform.position;
            deadYaw = transform.eulerAngles.y;
            animator.SetFloat(SpeedId, velocity.magnitude, speedDamping, deltaTime);

            if (Time.time >= nextSwingTime)
            {
                animator.SetTrigger(rightNext ? AttackRId : AttackLId);
                rightNext = !rightNext;
                nextSwingTime = float.PositiveInfinity;
            }
        }

        private void OnStateChanged(MeleeEnemy.State state)
        {
            if (state == MeleeEnemy.State.Dead)
            {
                dead = true;
                nextSwingTime = float.PositiveInfinity;
                deadGround = transform.position - Vector3.up * deathLift;
                animator.ResetTrigger(HitId);
                animator.SetTrigger(DieId);
                return;
            }

            if (state == MeleeEnemy.State.Attacking)
            {
                var windup = config != null ? config.attackWindup : impactTime;
                nextSwingTime = Time.time + Mathf.Max(0f, windup - impactTime);
            }
            else
            {
                nextSwingTime = float.PositiveInfinity;
            }
        }

        private void OnAttacked()
        {
            if (enemy.CurrentState != MeleeEnemy.State.Attacking) return;
            var interval = config != null ? config.attackInterval : 1f;
            nextSwingTime = Time.time + Mathf.Max(0f, interval - impactTime);
        }

        private void OnDamaged(float amount)
        {
            // 爪击过程中不打断，避免命中帧和伤害对不上。
            if (dead || health.IsDead || animator == null) return;
            if (animator.GetCurrentAnimatorStateInfo(0).tagHash == AttackTag || animator.IsInTransition(0)) return;
            animator.SetTrigger(HitId);
        }
    }
}
