using System;
using UnityEngine;
using UnityEngine.AI;

namespace WildernessExtraction
{
    /// <summary>
    /// NavMesh 近战怪物（《敌人与 AI Bot 行为策划案》第 4 节）：
    /// 巡逻 → 视觉/声音/血迹感知 → 短暂警觉 → 追击 → 攻击 → 丢失目标后去最后目击点搜索 → 返回巡逻。
    /// 视觉需同时满足距离、正面视锥与无墙体遮挡；近身感知半径很小。
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(Health))]
    public sealed class MeleeEnemy : MonoBehaviour
    {
        public enum State
        {
            /// <summary>没有巡逻路线时原地待机。</summary>
            Idle,
            Investigating,
            Alerted,
            Chasing,
            Attacking,
            Dead,
            /// <summary>在巡逻点之间移动或在巡逻点短暂停留。</summary>
            Patrolling,
            /// <summary>到达最后目击点后原地环顾。</summary>
            Searching,
        }

        private const float RepathInterval = 0.2f;
        private const float AttackRangeHysteresis = 1.15f;
        private const float TargetBodyHeight = 1f;
        private const float SearchTurnSpeed = 120f;

        [SerializeField] private EnemyConfig config;
        [Tooltip("血迹循迹规则；为空时不循迹。")]
        [SerializeField] private BleedingConfig trailRules;
        [Tooltip("按顺序循环的巡逻点（2–5 个）。为空时原地待机。")]
        [SerializeField] private Transform[] patrolPoints;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Color idleColor = new Color(0.75f, 0.2f, 0.2f);
        [SerializeField] private Color investigateColor = new Color(0.95f, 0.85f, 0.2f);
        [SerializeField] private Color alertColor = new Color(1f, 0.45f, 0.1f);
        [SerializeField] private Color searchColor = new Color(0.85f, 0.6f, 0.9f);
        [SerializeField] private Color deadColor = new Color(0.3f, 0.3f, 0.3f);

        private NavMeshAgent agent;
        private Health health;
        // 当前锁定的目标（玩家或 AI Bot），以及正在确认中的候选目标。
        private CombatTarget target;
        private CombatTarget candidate;
        private float stateTime;
        private float nextAttackTime;
        private float nextRepathTime;
        private Vector3 investigationPoint;

        // 感知
        private float nextPerceptionTime;
        private bool targetVisible;
        private float visibleTime;
        private Vector3 lastSeenPosition;
        private float lastSeenTime = float.NegativeInfinity;

        // 巡逻
        private int patrolIndex;
        private bool waitingAtPatrolPoint;
        private float patrolProgressTime;
        private float patrolBestDistance;

        // 血迹
        private float lastFollowedMarkTime = float.NegativeInfinity;
        private float followingMarkTime;
        private float trailIdleTime;

        public event Action<State> StateChanged;
        public event Action Attacked;

        public State CurrentState { get; private set; } = State.Idle;
        public Health Health => health;
        /// <summary>正在沿血迹调查（Investigating 的一种来源）。</summary>
        public bool IsFollowingTrail { get; private set; }
        public Vector3 InvestigationPoint => investigationPoint;
        public bool TargetVisible => targetVisible;
        public Vector3 LastSeenPosition => lastSeenPosition;
        public bool HasPatrolRoute => patrolPoints != null && patrolPoints.Length > 0;
        public int PatrolIndex => patrolIndex;
        public CombatTarget CurrentTarget => target;

        private float SightRange => config.alertRadius;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            health.Configure(config.maxHealth);
            agent.speed = config.moveSpeed;
            agent.stoppingDistance = config.attackRange * 0.8f;
            ApplyColor(idleColor);
        }

        private void Start()
        {
            if (HasPatrolRoute)
            {
                patrolIndex = NearestPatrolIndex();
                BeginPatrol();
            }
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

        /// <summary>替换巡逻路线（测试或关卡脚本使用）。传入空数组则原地待机。</summary>
        public void SetPatrolRoute(Transform[] points)
        {
            patrolPoints = points;
            if (CurrentState == State.Patrolling || CurrentState == State.Idle) ReturnToRoutine();
        }

        private void Update()
        {
            if (CurrentState == State.Dead)
            {
                return;
            }

            stateTime += Time.deltaTime;

            if (target != null && !target.IsValid)
            {
                // 目标死亡或已撤离：立即清除目标并返回巡逻。
                target = null;
                targetVisible = false;
                visibleTime = 0f;
                if (IsEngaged(CurrentState) || CurrentState == State.Searching) ReturnToRoutine();
            }
            else if (target == null && IsEngaged(CurrentState))
            {
                ReturnToRoutine();
            }

            UpdatePerception();

            switch (CurrentState)
            {
                case State.Idle:
                case State.Patrolling:
                    if (DetectionConfirmed()) { SetState(State.Alerted); break; }
                    if (TryFollowTrail()) break;
                    UpdateRoutine();
                    break;

                case State.Investigating:
                    if (DetectionConfirmed()) { SetState(State.Alerted); break; }
                    if (IsFollowingTrail) UpdateTrail();
                    else UpdateInvestigation();
                    break;

                case State.Searching:
                    if (DetectionConfirmed()) { SetState(State.Alerted); break; }
                    transform.Rotate(0f, SearchTurnSpeed * Time.deltaTime, 0f);
                    if (stateTime >= config.searchDuration) ReturnToRoutine();
                    break;

                case State.Alerted:
                    FaceTarget();
                    if (stateTime >= config.reactionTime) SetState(State.Chasing);
                    break;

                case State.Chasing:
                    UpdateChase();
                    break;

                case State.Attacking:
                    UpdateAttack();
                    break;
            }
        }

        // ---------- 感知 ----------

        private void UpdatePerception()
        {
            if (Time.time < nextPerceptionTime)
            {
                if (targetVisible) visibleTime += Time.deltaTime;
                return;
            }

            nextPerceptionTime = Time.time + config.perceptionInterval;
            var wasVisible = targetVisible;

            if (IsEngaged(CurrentState) && target != null)
            {
                // 追击中只跟踪当前目标（“当前可见攻击目标”优先）。
                targetVisible = EnemyPerception.CanTrack(transform.position, target.transform.position, config.chaseSightRange,
                    HasLineOfSight(target));
                if (targetVisible) RememberSighting(target);
                visibleTime = targetVisible ? visibleTime + Time.deltaTime : 0f;
                return;
            }

            // 未警戒：在所有有效目标中找最近的可见者作为候选；候选变化时重新确认。
            CombatTarget nearest = null;
            var nearestDistance = float.MaxValue;
            foreach (var other in CombatTarget.Active)
            {
                if (!other.IsValid) continue;
                var position = other.transform.position;
                if (!EnemyPerception.CanDetect(transform.position, transform.forward, position, SightRange,
                        config.fieldOfView, config.proximityRadius, HasLineOfSight(other)))
                {
                    continue;
                }

                var distance = HorizontalDistanceTo(position);
                if (distance < nearestDistance)
                {
                    nearest = other;
                    nearestDistance = distance;
                }
            }

            targetVisible = nearest != null;
            visibleTime = targetVisible && wasVisible && nearest == candidate ? visibleTime + Time.deltaTime : 0f;
            candidate = nearest;
            if (targetVisible) RememberSighting(nearest);
        }

        private void RememberSighting(CombatTarget seen)
        {
            lastSeenPosition = seen.transform.position;
            lastSeenTime = Time.time;
        }

        /// <summary>候选目标连续可见满确认时间才算发现；近身感知立即发现。发现后锁定为当前目标。</summary>
        private bool DetectionConfirmed()
        {
            if (!targetVisible || candidate == null) return false;
            if (visibleTime < config.detectionConfirmTime
                && HorizontalDistanceTo(candidate.transform.position) > config.proximityRadius)
            {
                return false;
            }

            target = candidate;
            return true;
        }

        private bool HasLineOfSight(CombatTarget other)
        {
            var eye = transform.position + Vector3.up * config.eyeHeight;
            var body = other.transform.position + Vector3.up * TargetBodyHeight;
            if (!Physics.Linecast(eye, body, out var hit, config.sightMask, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            return hit.transform == other.transform || hit.transform.IsChildOf(other.transform);
        }

        private static bool IsEngaged(State state)
        {
            return state == State.Alerted || state == State.Chasing || state == State.Attacking;
        }

        // ---------- 追击与攻击 ----------

        private void UpdateChase()
        {
            if (targetVisible)
            {
                if (HorizontalDistanceTo(target.transform.position) <= config.attackRange)
                {
                    StopMoving();
                    nextAttackTime = Mathf.Max(nextAttackTime, Time.time + config.attackWindup);
                    SetState(State.Attacking);
                    return;
                }

                MoveTo(target.transform.position, config.moveSpeed);
                return;
            }

            // 目标离开视野：在记忆时间内继续前往最后目击点；到达或超时则原地搜索。
            var reached = HorizontalDistanceTo(lastSeenPosition) <= ArrivalDistance;
            if (reached || Time.time - lastSeenTime >= config.targetMemoryDuration)
            {
                StopMoving();
                SetState(State.Searching);
                return;
            }

            MoveTo(lastSeenPosition, config.moveSpeed);
        }

        private void UpdateAttack()
        {
            FaceTarget();
            var distance = HorizontalDistanceTo(target.transform.position);
            if (!targetVisible || distance > config.attackRange * AttackRangeHysteresis)
            {
                SetState(State.Chasing);
                return;
            }

            if (Time.time < nextAttackTime) return;
            nextAttackTime = Time.time + config.attackInterval;
            // 隔墙或隔掩体不造成伤害。
            if (!HasLineOfSight(target)) return;
            if (target.Health.TakeDamage(config.damage, gameObject) && target.Bleeding != null)
            {
                target.Bleeding.Bleed();
            }

            Attacked?.Invoke();
        }

        // ---------- 声音、受伤与调查 ----------

        private void OnDamaged(float amount)
        {
            if (health.IsDead || IsEngaged(CurrentState)) return;
            // 最近受伤来源：视为知道攻击者（玩家或人机）位置，进入警觉；来源未知时按玩家处理。
            var source = health.LastDamageSource;
            var attacker = source != null ? source.GetComponentInParent<CombatTarget>() : null;
            if (attacker == null) attacker = CombatTarget.FindPlayer();
            if (attacker == null || !attacker.IsValid) return;
            target = attacker;
            candidate = attacker;
            RememberSighting(attacker);
            targetVisible = HasLineOfSight(attacker);
            SetState(State.Alerted);
        }

        private void OnNoise(Vector3 position, float radius)
        {
            if (CurrentState == State.Dead || IsEngaged(CurrentState) || HorizontalDistanceTo(position) > radius) return;
            // 前往声源调查，而不是直接锁定发声者；新的声音覆盖旧的调查点。
            investigationPoint = position;
            IsFollowingTrail = false;
            SetState(State.Investigating);
            nextRepathTime = 0f;
        }

        private void UpdateInvestigation()
        {
            if (HorizontalDistanceTo(investigationPoint) <= ArrivalDistance)
            {
                StopMoving();
                SetState(State.Searching);
                return;
            }

            MoveTo(investigationPoint, config.moveSpeed);
        }

        // ---------- 血迹 ----------

        /// <summary>未发现玩家时，按旧到新寻找可察觉的血迹并前往调查。返回是否找到。</summary>
        private bool TryFollowTrail()
        {
            if (trailRules == null)
            {
                return false;
            }

            if (!BloodTrailRegistry.Scene.TryFindOldestAfter(transform.position, trailRules.scentRange, lastFollowedMarkTime, Time.time,
                    IsScentMasked, out var mark))
            {
                return false;
            }

            investigationPoint = mark.Position;
            followingMarkTime = mark.CreatedAt;
            trailIdleTime = 0f;
            SetState(State.Investigating);
            IsFollowingTrail = true;
            nextRepathTime = 0f;
            return true;
        }

        private void UpdateTrail()
        {
            // 血迹“变冷”（察觉范围内没有比当前目标更新的血迹）后开始计时；
            // 连续该秒数仍未看到玩家 → 放弃，并忽略当前已有的全部血迹。
            if (BloodTrailRegistry.Scene.TryFindOldestAfter(transform.position, trailRules.scentRange, followingMarkTime, Time.time, IsScentMasked, out _))
            {
                trailIdleTime = 0f;
            }
            else
            {
                trailIdleTime += Time.deltaTime;
            }

            if (trailIdleTime >= trailRules.giveUpWithoutSight)
            {
                GiveUpTrail();
                return;
            }

            if (HorizontalDistanceTo(investigationPoint) <= ArrivalDistance)
            {
                lastFollowedMarkTime = followingMarkTime;
                if (!TryFollowTrail())
                {
                    GiveUpTrail();
                }

                return;
            }

            MoveTo(investigationPoint, config.moveSpeed);
        }

        private void GiveUpTrail()
        {
            lastFollowedMarkTime = Time.time;
            ReturnToRoutine();
        }

        private static bool IsScentMasked(Vector3 position)
        {
            // 有效光（安全屋、固定灯、火堆）内的血迹气味被遮蔽。
            return LightRegistry.Scene.IsLit(position);
        }

        // ---------- 巡逻 ----------

        /// <summary>调查、搜索或追击结束后：有巡逻路线则回到最近的巡逻点，否则原地待机。</summary>
        private void ReturnToRoutine()
        {
            StopMoving();
            if (HasPatrolRoute)
            {
                patrolIndex = NearestPatrolIndex();
                BeginPatrol();
            }
            else
            {
                SetState(State.Idle);
            }
        }

        private void BeginPatrol()
        {
            waitingAtPatrolPoint = false;
            patrolProgressTime = 0f;
            patrolBestDistance = float.MaxValue;
            nextRepathTime = 0f;
            SetState(State.Patrolling);
        }

        private void UpdateRoutine()
        {
            if (CurrentState != State.Patrolling || !HasPatrolRoute) return;

            var point = patrolPoints[patrolIndex];
            if (point == null)
            {
                AdvancePatrol();
                return;
            }

            if (waitingAtPatrolPoint)
            {
                FaceTowards(patrolPoints[(patrolIndex + 1) % patrolPoints.Length]);
                if (stateTime >= config.patrolWaitDuration) AdvancePatrol();
                return;
            }

            var distance = HorizontalDistanceTo(point.position);
            if (distance <= ArrivalDistance)
            {
                StopMoving();
                waitingAtPatrolPoint = true;
                stateTime = 0f;
                return;
            }

            // 路径不可达或长时间没有进展：放弃该点，去下一个。
            if (distance < patrolBestDistance - 0.25f)
            {
                patrolBestDistance = distance;
                patrolProgressTime = 0f;
            }
            else
            {
                patrolProgressTime += Time.deltaTime;
            }

            var unreachable = agent.isOnNavMesh && !agent.pathPending && agent.hasPath && agent.pathStatus != NavMeshPathStatus.PathComplete;
            if (patrolProgressTime >= config.patrolStuckTimeout || unreachable)
            {
                AdvancePatrol();
                return;
            }

            MoveTo(point.position, config.moveSpeed * config.patrolSpeedMultiplier);
        }

        private void AdvancePatrol()
        {
            patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
            BeginPatrol();
            stateTime = 0f;
        }

        private int NearestPatrolIndex()
        {
            var best = 0;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < patrolPoints.Length; i++)
            {
                if (patrolPoints[i] == null) continue;
                var distance = HorizontalDistanceTo(patrolPoints[i].position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        // ---------- 通用 ----------

        private float ArrivalDistance => Mathf.Max(0.8f, agent.stoppingDistance + 0.3f);

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
            if (state != State.Investigating)
            {
                IsFollowingTrail = false;
            }

            switch (state)
            {
                case State.Investigating:
                    ApplyColor(investigateColor);
                    break;
                case State.Searching:
                    ApplyColor(searchColor);
                    break;
                case State.Alerted:
                case State.Chasing:
                case State.Attacking:
                    ApplyColor(alertColor);
                    break;
                case State.Idle:
                case State.Patrolling:
                    ApplyColor(idleColor);
                    break;
            }

            StateChanged?.Invoke(state);
        }

        private void MoveTo(Vector3 destination, float speed)
        {
            if (!agent.isOnNavMesh)
            {
                return;
            }

            agent.speed = speed;
            agent.isStopped = false;
            if (Time.time >= nextRepathTime)
            {
                nextRepathTime = Time.time + RepathInterval;
                agent.SetDestination(destination);
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
            if (target != null) FaceTowards(target.transform);
        }

        private void FaceTowards(Transform other)
        {
            if (other == null) return;
            var toTarget = other.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toTarget, Vector3.up), 360f * Time.deltaTime);
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

        // 开发模式调试：视锥、近身半径、当前目标点。
        private void OnDrawGizmos()
        {
            if (config == null) return;
            var origin = transform.position + Vector3.up * 0.1f;
            Gizmos.color = CurrentState == State.Chasing || CurrentState == State.Attacking ? Color.red
                : CurrentState == State.Investigating || CurrentState == State.Searching ? Color.yellow
                : new Color(1f, 1f, 1f, 0.6f);
            var half = config.fieldOfView * 0.5f;
            var left = Quaternion.Euler(0f, -half, 0f) * transform.forward * SightRange;
            var right = Quaternion.Euler(0f, half, 0f) * transform.forward * SightRange;
            Gizmos.DrawLine(origin, origin + left);
            Gizmos.DrawLine(origin, origin + right);
            const int segments = 16;
            var previous = origin + left;
            for (var i = 1; i <= segments; i++)
            {
                var next = origin + Quaternion.Euler(0f, -half + config.fieldOfView * i / segments, 0f) * transform.forward * SightRange;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }

            Gizmos.DrawWireSphere(transform.position, config.proximityRadius);
            if (CurrentState == State.Investigating) Gizmos.DrawLine(origin, investigationPoint);
            if (IsEngaged(CurrentState) || CurrentState == State.Searching) Gizmos.DrawLine(origin, lastSeenPosition);
        }
    }
}
