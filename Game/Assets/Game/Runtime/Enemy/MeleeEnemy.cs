using System;
using UnityEngine;
using UnityEngine.AI;

namespace WildernessExtraction
{
    /// <summary>NavMesh 近战敌人：待机 → 发现 → 追逐 → 攻击 → 死亡。</summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(Health))]
    public sealed class MeleeEnemy : MonoBehaviour
    {
        public enum State
        {
            Idle,
            Investigating,
            Alerted,
            Chasing,
            Attacking,
            Dead
        }

        private const float RepathInterval = 0.2f;
        private const float AttackRangeHysteresis = 1.15f;

        [SerializeField] private EnemyConfig config;
        [Tooltip("为空时自动查找场景中的 PlayerCharacter。")]
        [SerializeField] private Health target;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Color idleColor = new Color(0.75f, 0.2f, 0.2f);
        [SerializeField] private Color alertColor = new Color(1f, 0.55f, 0.1f);
        [SerializeField] private Color deadColor = new Color(0.3f, 0.3f, 0.3f);

        private NavMeshAgent agent;
        private Health health;
        private float stateTime;
        private float nextAttackTime;
        private float nextRepathTime;
        private Vector3 investigationPoint;

        public event Action<State> StateChanged;
        public event Action Attacked;

        public State CurrentState { get; private set; } = State.Idle;
        public Health Health => health;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            health.Configure(config.maxHealth);
            agent.speed = config.moveSpeed;
            agent.stoppingDistance = config.attackRange * 0.8f;

            if (target == null)
            {
                var player = FindAnyObjectByType<PlayerCharacter>();
                if (player != null)
                {
                    target = player.Health;
                }
            }

            ApplyColor(idleColor);
        }

        private void OnEnable()
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
            CombatNoise.Emitted += OnNoise;
        }

        private void OnDisable()
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
            CombatNoise.Emitted -= OnNoise;
        }

        private void Update()
        {
            if (CurrentState == State.Dead)
            {
                return;
            }

            stateTime += Time.deltaTime;

            if (target == null || target.IsDead)
            {
                if (CurrentState != State.Idle)
                {
                    StopMoving();
                    SetState(State.Idle);
                }

                return;
            }

            var distance = HorizontalDistanceTo(target.transform.position);
            switch (CurrentState)
            {
                case State.Idle:
                    if (distance <= config.alertRadius)
                    {
                        SetState(State.Alerted);
                    }

                    break;

                case State.Investigating:
                    if (distance <= config.alertRadius)
                    {
                        SetState(State.Alerted);
                    }
                    else if (HorizontalDistanceTo(investigationPoint) < 0.8f)
                    {
                        StopMoving();
                        SetState(State.Idle);
                    }
                    else if (agent.isOnNavMesh && Time.time >= nextRepathTime)
                    {
                        nextRepathTime = Time.time + RepathInterval;
                        agent.isStopped = false;
                        agent.SetDestination(investigationPoint);
                    }
                    break;

                case State.Alerted:
                    FaceTarget();
                    if (stateTime >= config.reactionTime)
                    {
                        SetState(State.Chasing);
                    }

                    break;

                case State.Chasing:
                    if (distance <= config.attackRange)
                    {
                        StopMoving();
                        nextAttackTime = Mathf.Max(nextAttackTime, Time.time + config.attackWindup);
                        SetState(State.Attacking);
                    }
                    else
                    {
                        ChaseTarget();
                    }

                    break;

                case State.Attacking:
                    FaceTarget();
                    if (distance > config.attackRange * AttackRangeHysteresis)
                    {
                        SetState(State.Chasing);
                    }
                    else if (Time.time >= nextAttackTime)
                    {
                        nextAttackTime = Time.time + config.attackInterval;
                        target.TakeDamage(config.damage);
                        Attacked?.Invoke();
                    }

                    break;
            }
        }

        private void OnDamaged(float amount)
        {
            if (CurrentState == State.Idle && !health.IsDead)
            {
                SetState(State.Alerted);
            }
        }

        private void OnNoise(Vector3 position, float radius)
        {
            if (CurrentState == State.Dead || HorizontalDistanceTo(position) > radius) return;
            investigationPoint = position;
            if (CurrentState == State.Idle || CurrentState == State.Investigating) SetState(State.Investigating);
        }

        private void OnDied()
        {
            StopMoving();
            agent.enabled = false;
            foreach (var hitCollider in GetComponentsInChildren<Collider>())
            {
                hitCollider.enabled = false;
            }

            // 灰盒死亡表现：倒地并变灰。
            transform.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);
            transform.position += Vector3.up * 0.4f;
            ApplyColor(deadColor);
            SetState(State.Dead);
        }

        private void SetState(State state)
        {
            if (CurrentState == state)
            {
                return;
            }

            CurrentState = state;
            stateTime = 0f;
            if (state == State.Investigating || state == State.Alerted || state == State.Chasing || state == State.Attacking)
            {
                ApplyColor(alertColor);
            }
            else if (state == State.Idle)
            {
                ApplyColor(idleColor);
            }

            StateChanged?.Invoke(state);
        }

        private void ChaseTarget()
        {
            if (!agent.isOnNavMesh)
            {
                return;
            }

            agent.isStopped = false;
            if (Time.time >= nextRepathTime)
            {
                nextRepathTime = Time.time + RepathInterval;
                agent.SetDestination(target.transform.position);
            }
        }

        private void StopMoving()
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }

            nextRepathTime = 0f;
        }

        private void FaceTarget()
        {
            var toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(toTarget, Vector3.up);
            }
        }

        private float HorizontalDistanceTo(Vector3 position)
        {
            var delta = position - transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        private void ApplyColor(Color color)
        {
            if (bodyRenderer != null)
            {
                bodyRenderer.material.color = color;
            }
        }
    }
}
