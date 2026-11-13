using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace WildernessExtraction
{
    /// <summary>
    /// 敌对人机（类似塔科夫 Scav）：对玩家敌对，与怪物互相敌对，人机之间不互相攻击。
    /// 在出生点周围的地盘内巡逻于光源之间、搜刮看得见的容器；用与玩家相同的枪械规则射击，弹药有限；
    /// 与怪物同一套视觉规则（距离、视锥、遮挡）；同样怕黑；残血时撤回地盘内的光区。
    /// 不使用玩家的安全屋和撤离点；死亡后留下可搜索的尸体（枪、剩余弹药和搜刮所得）。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent), typeof(Health), typeof(CombatTarget))]
    public sealed class AiBotController : MonoBehaviour
    {
        public enum State
        {
            Idle,
            Wander,
            Loot,
            Search,
            Alert,
            Engage,
            Seek,
            Retreat,
            Dead,
        }

        private const float TargetBodyHeight = 1f;
        private const float TracerDuration = 0.06f;
        private const float LookAroundSpeed = 60f;

        [SerializeField] private AiBotConfig config;
        [SerializeField] private RunRulesConfig rules;
        [SerializeField] private DarknessConfig darkness;
        [SerializeField] private WeaponConfig weapon;
        [Tooltip("尸体中掉落的武器物品。")]
        [SerializeField] private ItemDefinition weaponItem;
        [SerializeField] private string botName = "人机";
        [SerializeField] private Transform muzzle;
        [SerializeField] private LineRenderer tracer;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private TextMesh debugLabel;
        [SerializeField] private Color idleColor = new Color(0.55f, 0.6f, 0.35f);
        [SerializeField] private Color combatColor = new Color(0.95f, 0.35f, 0.2f);
        [SerializeField] private Color deadColor = new Color(0.3f, 0.3f, 0.3f);

        private readonly List<SafeLightSource> territoryLights = new List<SafeLightSource>();
        private readonly HashSet<LootContainer> finishedContainers = new HashSet<LootContainer>();
        private readonly Dictionary<long, float> pathCache = new Dictionary<long, float>();
        private readonly List<Vector3> route = new List<Vector3>();
        private readonly List<float> routeRadii = new List<float>();
        private readonly TimedProgress search = new TimedProgress();
        private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

        private NavMeshAgent agent;
        private Health health;
        private LightExposure exposure;
        private Inventory inventory;
        private WeaponState gun;
        private NavMeshPath scratchPath;
        private MeleeEnemy[] monsters = Array.Empty<MeleeEnemy>();
        private Vector3 anchor;
        private float nextThinkTime;
        private float stateTime;
        private int routeIndex;
        private float progressTime;
        private float bestDistance;
        private LootContainer goalContainer;
        private SafeLightSource goalLight;

        // 目标
        private Transform target;
        private Health targetHealth;
        private Transform candidate;
        private bool targetVisible;
        private float visibleTime;
        private Vector3 lastSeenPosition;
        private float lastSeenTime = float.NegativeInfinity;
        private float aimReadyTime;
        private float tracerHideTime;

        public State CurrentState { get; private set; } = State.Idle;
        public Inventory Inventory => inventory;
        public bool IsAlive => CurrentState != State.Dead;
        public Transform Target => target;
        public bool TargetVisible => targetVisible;
        public Vector3 Anchor => anchor;
        public int AmmoInMagazine => gun != null ? gun.AmmoInMagazine : 0;
        public int ReserveAmmo => weapon != null && weapon.ammoItem != null ? inventory.CountOf(weapon.ammoItem) : 0;
        public IReadOnlyCollection<LootContainer> FinishedContainers => finishedContainers;
        public bool IsSearching => CurrentState == State.Search && search.IsRunning;
        public string BotName => botName;
        public event Action<AiBotController> Fired;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            exposure = GetComponent<LightExposure>();
            scratchPath = new NavMeshPath();
            health.Configure(config.maxHealth);
            inventory = new Inventory(rules.inventoryCapacity);
            agent.speed = config.moveSpeed;
            agent.stoppingDistance = 0.2f;
            gun = new WeaponState(Mathf.Max(1, weapon.magazineSize), weapon.fireInterval, weapon.reloadDuration);
            if (weapon.ammoItem != null)
            {
                for (var i = 0; i < config.startingAmmo; i++) inventory.TryAdd(weapon.ammoItem);
            }

            if (debugLabel != null) debugLabel.gameObject.SetActive(config.showDebugLabel);
            if (tracer != null) tracer.enabled = false;
            ApplyColor(idleColor);
        }

        private void Start()
        {
            anchor = transform.position;
            monsters = FindObjectsByType<MeleeEnemy>();
            foreach (var area in LightRegistry.Scene.Areas)
            {
                if (area is SafeLightSource light && light.Kind != SafeLightKind.SafeHouse && InTerritory(light.transform.position))
                {
                    territoryLights.Add(light);
                }
            }

            SetState(State.Idle);
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

        /// <summary>修改地盘中心（测试或关卡脚本使用）。</summary>
        public void SetAnchor(Vector3 position)
        {
            anchor = position;
        }

        private void Update()
        {
            if (!IsAlive) return;
            stateTime += Time.deltaTime;
            // 换弹完成时，装入弹匣的子弹从背包备弹中真实扣除（与玩家规则一致）。
            var magazineBefore = gun.AmmoInMagazine;
            gun.Tick(Time.time);
            if (gun.AmmoInMagazine > magazineBefore && weapon.ammoItem != null)
            {
                inventory.Remove(weapon.ammoItem, gun.AmmoInMagazine - magazineBefore);
            }

            if (tracer != null && tracer.enabled && Time.time >= tracerHideTime) tracer.enabled = false;

            if (Time.time >= nextThinkTime)
            {
                nextThinkTime = Time.time + config.thinkInterval;
                Perceive();
                Think();
                RefreshLabel();
            }
            else if (targetVisible)
            {
                visibleTime += Time.deltaTime;
            }

            switch (CurrentState)
            {
                case State.Wander:
                case State.Loot:
                    FollowRoute();
                    break;
                case State.Search:
                    UpdateSearch();
                    break;
                case State.Idle:
                    transform.Rotate(0f, LookAroundSpeed * Time.deltaTime, 0f);
                    break;
                case State.Alert:
                case State.Engage:
                    UpdateCombat();
                    break;
            }
        }

        // ---------- 感知 ----------

        private void Perceive()
        {
            var wasVisible = targetVisible;
            if (target != null && IsEngaged)
            {
                targetVisible = TargetAlive(targetHealth) && EnemyPerception.CanTrack(transform.position, target.position,
                    config.combatSightRange, HasLineOfSight(target));
                if (targetVisible)
                {
                    lastSeenPosition = target.position;
                    lastSeenTime = Time.time;
                }

                return;
            }

            // 未交战：在玩家与怪物中找最近的可见者作为候选，连续可见满确认时间才算发现。
            Transform nearest = null;
            var nearestDistance = float.MaxValue;
            foreach (var hostile in Hostiles())
            {
                if (!EnemyPerception.CanDetect(transform.position, transform.forward, hostile.position, config.sightRange,
                        config.fieldOfView, config.proximityRadius, HasLineOfSight(hostile)))
                {
                    continue;
                }

                var distance = Distance(hostile.position);
                if (distance < nearestDistance)
                {
                    nearest = hostile;
                    nearestDistance = distance;
                }
            }

            targetVisible = nearest != null;
            visibleTime = targetVisible && wasVisible && nearest == candidate ? visibleTime + config.thinkInterval : 0f;
            candidate = nearest;
            if (targetVisible)
            {
                lastSeenPosition = nearest.position;
                lastSeenTime = Time.time;
            }
        }

        private IEnumerable<Transform> Hostiles()
        {
            var player = CombatTarget.FindPlayer();
            if (player != null && player.IsValid) yield return player.transform;
            foreach (var monster in monsters)
            {
                if (monster != null && monster.isActiveAndEnabled && monster.CurrentState != MeleeEnemy.State.Dead) yield return monster.transform;
            }
        }

        private bool DetectionConfirmed()
        {
            if (!targetVisible || candidate == null) return false;
            return visibleTime >= config.detectionConfirmTime || Distance(candidate.position) <= config.proximityRadius;
        }

        private bool HasLineOfSight(Transform other)
        {
            var eye = transform.position + Vector3.up * config.eyeHeight;
            var body = other.position + Vector3.up * TargetBodyHeight;
            if (!Physics.Linecast(eye, body, out var hit, config.sightMask, QueryTriggerInteraction.Ignore)) return true;
            if (hit.transform.IsChildOf(transform)) return true;
            return hit.transform == other || hit.transform.IsChildOf(other);
        }

        private static bool TargetAlive(Health other)
        {
            if (other == null || other.IsDead || !other.isActiveAndEnabled) return false;
            var player = other.GetComponent<CombatTarget>();
            return player == null || player.IsValid;
        }

        private bool IsEngaged => CurrentState == State.Alert || CurrentState == State.Engage;

        // ---------- 决策 ----------

        private void Think()
        {
            if (IsEngaged && !TargetAlive(targetHealth))
            {
                // 目标死亡或已离场：回到日常。
                ClearTarget();
                SetState(State.Idle);
                return;
            }

            if (!IsEngaged && DetectionConfirmed())
            {
                Engage(candidate);
                return;
            }

            if (IsEngaged && !targetVisible)
            {
                // 丢失目标：在记忆时间内去最后目击点搜寻（仅限地盘内）。
                if (Time.time - lastSeenTime >= config.targetMemoryDuration || !InTerritory(lastSeenPosition))
                {
                    ClearTarget();
                    SetState(State.Idle);
                }
                else
                {
                    BeginMove(State.Seek, lastSeenPosition);
                }

                return;
            }

            // 怕黑：非交战时一旦开始受黑暗伤害，立即回到最近的光区。
            if (!IsEngaged && exposure != null && exposure.State == LightExposureState.Exposed && CurrentState != State.Retreat)
            {
                RetreatToLight();
                return;
            }

            switch (CurrentState)
            {
                case State.Idle:
                    if (health.Normalized < config.retreatHealthFraction && !LightRegistry.Scene.IsLit(transform.position)) RetreatToLight();
                    else if (stateTime >= config.postWaitDuration) DecideRoutine();
                    break;
                case State.Seek:
                case State.Retreat:
                    if (Arrived(route.Count > 0 ? route[route.Count - 1] : transform.position, 1.2f))
                    {
                        if (CurrentState == State.Seek) ClearTarget();
                        SetState(State.Idle);
                    }
                    else
                    {
                        FollowRoute();
                    }

                    break;
            }
        }

        private void DecideRoutine()
        {
            // 搜刮地盘内看得见、在光里能完整搜完的容器；否则换到另一处光源驻守观察。
            if (inventory.FreeSlots > 0)
            {
                LootContainer best = null;
                var bestScore = float.NegativeInfinity;
                foreach (var container in LootContainer.Active)
                {
                    if (container.ReservedForPlayer || container.IsEmpty || finishedContainers.Contains(container)) continue;
                    if (!InTerritory(container.transform.position) || !LightRegistry.Scene.IsLit(container.transform.position)) continue;
                    if (Distance(container.transform.position) > config.sightRange * 1.5f
                        && !HasLineOfSight(container.transform)) continue;
                    var score = AiBotRules.ContainerTypeScore(container.Type) - Distance(container.transform.position);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = container;
                    }
                }

                if (best != null && NavMesh.SamplePosition(best.transform.position, out var hit, config.interactRange, NavMesh.AllAreas))
                {
                    if (TryPlanRoute(transform.position, hit.position, config.routeDamageBudget, out _, out _))
                    {
                        goalContainer = best;
                        BeginMove(State.Loot, hit.position);
                        return;
                    }

                    // 怕黑：到不了（途中黑暗超出承受范围）的容器本局不再考虑。
                    finishedContainers.Add(best);
                }
            }

            SafeLightSource next = null;
            var nextScore = float.NegativeInfinity;
            foreach (var light in territoryLights)
            {
                if (light == null || !light.IsLit || light.Contains(transform.position)) continue;
                if (!TryPlanRoute(transform.position, light.transform.position, config.routeDamageBudget, out _, out _)) continue;
                var score = UnityEngine.Random.value * 10f - Distance(light.transform.position) * 0.2f;
                if (score > nextScore)
                {
                    nextScore = score;
                    next = light;
                }
            }

            if (next != null)
            {
                goalLight = next;
                BeginMove(State.Wander, next.transform.position);
            }
            else
            {
                SetState(State.Idle);
            }
        }

        private void Engage(Transform hostile)
        {
            target = hostile;
            targetHealth = hostile.GetComponent<Health>();
            CancelSearch();
            aimReadyTime = Time.time + config.aimDelay;
            SetState(State.Alert);
            ApplyColor(combatColor);
        }

        private void ClearTarget()
        {
            target = null;
            targetHealth = null;
            candidate = null;
            targetVisible = false;
            visibleTime = 0f;
            ApplyColor(idleColor);
        }

        // ---------- 战斗 ----------

        private void UpdateCombat()
        {
            if (target == null) return;
            FaceTowards(target.position);
            var distance = Distance(target.position);
            var isMonster = target.GetComponent<MeleeEnemy>() != null;
            var lowHealth = health.Normalized < config.retreatHealthFraction;
            var inDark = exposure != null && exposure.State != LightExposureState.Safe;

            // 站位：残血或站在黑暗里时边打边退向光区；离怪物太近时后撤；目标太远时（地盘内）靠近。
            if (lowHealth || inDark)
            {
                var light = NearestLight(target.position);
                if (light != null) MoveTo(light.transform.position);
            }
            else if (isMonster && distance < config.keepAwayFromMonster)
            {
                var away = transform.position - target.position;
                away.y = 0f;
                var point = transform.position + away.normalized * 4f;
                if (NavMesh.SamplePosition(point, out var hit, 3f, NavMesh.AllAreas)) MoveTo(hit.position);
            }
            else if (targetVisible && distance > config.preferredRange && InTerritory(target.position))
            {
                MoveTo(target.position);
            }
            else
            {
                Stop();
            }

            if (CurrentState == State.Alert)
            {
                if (Time.time >= aimReadyTime) SetState(State.Engage);
                return;
            }

            if (!targetVisible) return;
            TryShoot(distance);
        }

        private void TryShoot(float distance)
        {
            var now = Time.time;
            if (gun.IsReloading) return;
            if (gun.AmmoInMagazine == 0)
            {
                gun.TryStartReload(now, ReserveAmmo);
                return;
            }

            if (distance > weapon.range || !gun.TryFire(now)) return;

            var origin = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.3f;
            var aim = target.position + Vector3.up * TargetBodyHeight - origin;
            aim.y = 0f;
            var moving = agent.velocity.sqrMagnitude > 0.1f;
            var spread = AiBotRules.Spread(weapon.standingSpread, weapon.movingSpread, moving, config.spreadMultiplier);
            var direction = Quaternion.AngleAxis(UnityEngine.Random.Range(-spread, spread), Vector3.up) * aim.normalized;

            var end = origin + direction * weapon.range;
            if (TryFindHit(origin, direction, out var hit))
            {
                end = hit.point;
                var victim = hit.collider.GetComponentInParent<Health>();
                if (victim != null && victim.GetComponent<AiBotController>() == null)
                {
                    victim.TakeDamage(weapon.damage * AiBotRules.DamageFalloff(hit.distance, weapon.effectiveRange, weapon.range,
                        weapon.minimumDamageMultiplier), gameObject);
                }
            }

            ShowTracer(origin, end);
            CombatNoise.Emit(origin, weapon.soundRadius);
            Fired?.Invoke(this);

            if (gun.AmmoInMagazine == 0) gun.TryStartReload(now, ReserveAmmo);
        }

        private bool TryFindHit(Vector3 origin, Vector3 direction, out RaycastHit closest)
        {
            closest = default;
            var count = Physics.RaycastNonAlloc(origin, direction, hitBuffer, weapon.range, ~0, QueryTriggerInteraction.Ignore);
            var found = false;
            for (var i = 0; i < count; i++)
            {
                if (hitBuffer[i].collider.transform.IsChildOf(transform)) continue;
                if (!found || hitBuffer[i].distance < closest.distance)
                {
                    closest = hitBuffer[i];
                    found = true;
                }
            }

            return found;
        }

        private void ShowTracer(Vector3 from, Vector3 to)
        {
            if (tracer == null) return;
            tracer.positionCount = 2;
            tracer.SetPosition(0, from);
            tracer.SetPosition(1, to);
            tracer.enabled = true;
            tracerHideTime = Time.time + TracerDuration;
        }

        private void OnDamaged(float amount)
        {
            if (CurrentState == State.Search) CancelSearch();
            if (!IsAlive || IsEngaged) return;
            // 被打：转向攻击者（玩家或怪物）；黑暗伤害没有来源，不触发。
            var source = health.LastDamageSource;
            if (source == null || source.GetComponentInParent<AiBotController>() != null) return;
            lastSeenPosition = source.transform.position;
            lastSeenTime = Time.time;
            Engage(source.transform);
            targetVisible = HasLineOfSight(source.transform);
        }

        private void OnNoise(Vector3 position, float radius)
        {
            if (!IsAlive || IsEngaged || Distance(position) > radius || !InTerritory(position)) return;
            if (Distance(position) < 1f) return; // 自己的枪声
            // 声源在承受范围外的黑暗里：只转身警戒，不摸黑过去。
            if (!TryPlanRoute(transform.position, position, config.routeDamageBudget, out _, out _))
            {
                transform.rotation = Quaternion.LookRotation(Flatten(position - transform.position).normalized, Vector3.up);
                return;
            }

            CancelSearch();
            BeginMove(State.Seek, position);
        }

        // ---------- 搜刮 ----------

        private void UpdateSearch()
        {
            if (goalContainer == null || goalContainer.IsEmpty || goalContainer.HorizontalDistanceTo(transform.position) > config.interactRange)
            {
                if (goalContainer != null) finishedContainers.Add(goalContainer);
                CancelSearch();
                return;
            }

            if (goalContainer.IsSearched && !search.IsRunning)
            {
                LootGoal();
                return;
            }

            if (!search.Tick(Time.deltaTime)) return;
            goalContainer.MarkSearched();
            LootGoal();
        }

        private void LootGoal()
        {
            goalContainer.TransferTo(inventory);
            finishedContainers.Add(goalContainer);
            goalContainer = null;
            SetState(State.Idle);
        }

        private void CancelSearch()
        {
            if (CurrentState != State.Search) return;
            search.Cancel();
            SetState(State.Idle);
        }

        // ---------- 移动 ----------

        private void BeginMove(State state, Vector3 destination)
        {
            route.Clear();
            routeRadii.Clear();
            if (TryPlanRoute(transform.position, destination, config.routeDamageBudget, out var go, out var radii))
            {
                route.AddRange(go);
                routeRadii.AddRange(radii);
            }
            else
            {
                route.Add(destination);
                routeRadii.Add(0f);
            }

            routeIndex = 0;
            progressTime = 0f;
            bestDistance = float.MaxValue;
            SetState(state);
            MoveTo(route[0]);
        }

        private void FollowRoute()
        {
            if (routeIndex >= route.Count) return;
            var waypoint = route[routeIndex];
            var distance = Distance(waypoint);
            var isLast = routeIndex == route.Count - 1;
            var arrive = isLast
                ? (CurrentState == State.Loot && goalContainer != null ? goalContainer.HorizontalDistanceTo(transform.position) <= config.interactRange * 0.9f : distance <= 1.2f)
                : distance <= Mathf.Max(1.2f, routeRadii[routeIndex] * 0.5f);

            if (arrive)
            {
                if (!isLast)
                {
                    routeIndex++;
                    bestDistance = float.MaxValue;
                    MoveTo(route[routeIndex]);
                    return;
                }

                Stop();
                if (CurrentState == State.Loot && goalContainer != null)
                {
                    if (goalContainer.IsSearched) LootGoal();
                    else
                    {
                        search.Start(goalContainer.SearchDuration);
                        SetState(State.Search);
                    }
                }
                else if (CurrentState == State.Wander || CurrentState == State.Loot)
                {
                    SetState(State.Idle);
                }

                return;
            }

            if (distance < bestDistance - 0.2f)
            {
                bestDistance = distance;
                progressTime = 0f;
            }
            else if ((progressTime += Time.deltaTime) >= 6f)
            {
                if (goalContainer != null) finishedContainers.Add(goalContainer);
                goalContainer = null;
                SetState(State.Idle);
                return;
            }

            MoveTo(waypoint);
        }

        private void RetreatToLight()
        {
            var light = NearestLight(transform.position);
            if (light != null) BeginMove(State.Retreat, light.transform.position);
        }

        private SafeLightSource NearestLight(Vector3 awayFrom)
        {
            SafeLightSource best = null;
            var bestScore = float.NegativeInfinity;
            foreach (var light in territoryLights)
            {
                if (light == null || !light.IsLit) continue;
                var score = Vector3.Distance(light.transform.position, awayFrom) * 0.3f - Distance(light.transform.position);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = light;
                }
            }

            return best;
        }

        private bool TryPlanRoute(Vector3 from, Vector3 to, float maxDamage, out List<Vector3> waypoints, out List<float> radii)
        {
            waypoints = null;
            radii = null;
            var nodes = new List<RouteNode> { new RouteNode(from, DistanceToLightEdge(from)), new RouteNode(to, DistanceToLightEdge(to)) };
            foreach (var light in territoryLights)
            {
                if (light != null && light.IsLit) nodes.Add(new RouteNode(light.transform.position, light.Radius));
            }

            var planRules = new SafeRoutePlanner.Rules(config.moveSpeed,
                darkness != null ? darkness.warningDuration : 1.5f, darkness != null ? darkness.damagePerSecond : 20f, config.damageWeight);
            if (!SafeRoutePlanner.TryPlan(nodes, 0, 1, planRules, maxDamage, (a, b) => PathLength(nodes[a].Position, nodes[b].Position),
                    out var path, out _, out _))
            {
                return false;
            }

            waypoints = new List<Vector3>();
            radii = new List<float>();
            foreach (var index in path)
            {
                waypoints.Add(nodes[index].Position);
                radii.Add(index == 1 ? 0f : nodes[index].Radius);
            }

            return true;
        }

        private float PathLength(Vector3 a, Vector3 b)
        {
            var key = Key(a, b);
            if (pathCache.TryGetValue(key, out var cached)) return cached;
            var length = float.PositiveInfinity;
            if (NavMesh.SamplePosition(a, out var startHit, 2f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(b, out var endHit, 2f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, scratchPath)
                && scratchPath.status == NavMeshPathStatus.PathComplete)
            {
                length = 0f;
                var corners = scratchPath.corners;
                for (var i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            }

            pathCache[key] = length;
            return length;
        }

        private static long Key(Vector3 a, Vector3 b)
        {
            unchecked
            {
                long Quantize(Vector3 v) => ((long)Mathf.RoundToInt(v.x * 2f) & 0xFFFF) << 16 | ((long)Mathf.RoundToInt(v.z * 2f) & 0xFFFF);
                return Quantize(a) << 32 | Quantize(b);
            }
        }

        private static float DistanceToLightEdge(Vector3 position)
        {
            var best = 0f;
            foreach (var area in LightRegistry.Scene.Areas)
            {
                if (area is SafeLightSource source && source.IsLit && source.Contains(position))
                {
                    var delta = position - source.transform.position;
                    delta.y = 0f;
                    best = Mathf.Max(best, source.Radius - delta.magnitude);
                }
            }

            return best;
        }

        // ---------- 死亡 ----------

        private void OnDied()
        {
            Stop();
            agent.enabled = false;
            search.Cancel();
            ClearTarget();
            foreach (var hitCollider in GetComponentsInChildren<Collider>()) hitCollider.enabled = false;
            transform.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);
            transform.position += Vector3.up * 0.4f;
            ApplyColor(deadColor);
            SetState(State.Dead);
            if (debugLabel != null) debugLabel.gameObject.SetActive(false);

            // 留下可搜索的尸体：枪、弹匣与背包里的剩余弹药、搜刮所得。
            var drops = new List<ItemDefinition>();
            if (weaponItem != null) drops.Add(weaponItem);
            if (weapon.ammoItem != null)
            {
                for (var i = 0; i < gun.AmmoInMagazine; i++) drops.Add(weapon.ammoItem);
            }

            drops.AddRange(inventory.TakeAll());
            var corpse = gameObject.AddComponent<LootContainer>();
            corpse.InitializeWithItems($"{botName}的尸体", ContainerType.Corpse, 1.5f, drops);
        }

        // ---------- 通用 ----------

        private bool InTerritory(Vector3 position) => Vector3.Distance(Flatten(position), Flatten(anchor)) <= config.territoryRadius;

        private bool Arrived(Vector3 position, float tolerance) => Distance(position) <= tolerance;

        private void MoveTo(Vector3 destination)
        {
            if (!agent.enabled || !agent.isOnNavMesh) return;
            agent.isStopped = false;
            if ((agent.destination - destination).sqrMagnitude > 0.25f || !agent.hasPath) agent.SetDestination(destination);
        }

        private void Stop()
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
        }

        private void FaceTowards(Vector3 position)
        {
            var to = Flatten(position - transform.position);
            if (to.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to, Vector3.up), 540f * Time.deltaTime);
            }
        }

        private float Distance(Vector3 position) => Flatten(position - transform.position).magnitude;

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private void SetState(State state)
        {
            CurrentState = state;
            stateTime = 0f;
            if (state == State.Idle || state == State.Search) Stop();
            agent.updateRotation = state != State.Alert && state != State.Engage;
            RefreshLabel();
        }

        private void ApplyColor(Color color)
        {
            if (bodyRenderer != null) bodyRenderer.material.color = color;
        }

        private void RefreshLabel()
        {
            if (debugLabel == null || !debugLabel.gameObject.activeSelf) return;
            var detail = target != null ? $"→{target.name}" : goalContainer != null ? $"→{goalContainer.DisplayName}" : string.Empty;
            debugLabel.text = $"{botName} [{CurrentState}] {detail}\n生命 {Mathf.CeilToInt(health.Current)}  弹 {AmmoInMagazine}/{ReserveAmmo}";
        }

        private void LateUpdate()
        {
            if (debugLabel == null || !debugLabel.gameObject.activeSelf) return;
            var cam = Camera.main;
            if (cam != null) debugLabel.transform.rotation = cam.transform.rotation;
        }

        private void OnDrawGizmos()
        {
            if (config == null) return;
            var origin = transform.position + Vector3.up * 0.1f;
            Gizmos.color = IsEngaged ? Color.red : new Color(0.6f, 1f, 0.6f, 0.6f);
            var half = config.fieldOfView * 0.5f;
            Gizmos.DrawLine(origin, origin + Quaternion.Euler(0f, -half, 0f) * transform.forward * config.sightRange);
            Gizmos.DrawLine(origin, origin + Quaternion.Euler(0f, half, 0f) * transform.forward * config.sightRange);
            Gizmos.color = new Color(1f, 1f, 0.3f, 0.3f);
            Gizmos.DrawWireSphere(Application.isPlaying ? anchor : transform.position, config.territoryRadius);
            if (target != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(origin, target.position);
            }
        }
    }
}
