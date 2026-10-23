using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace WildernessExtraction.Map01
{
    public enum InteractionAction
    {
        Sell,
        Buy,
        RepairGenerator,
        ChargeBattery,
        DepositBattery
    }

    public enum InteractionResult
    {
        Accepted,
        UnknownEntity,
        OutOfRange,
        DuplicateSequence,
        WrongStation,
        NotEnoughCredits,
        InventoryFull,
        MissingItem,
        InvalidState,
        LimitReached,
        RunEnded
    }

    /// <summary>客户端只提交意图：实体 ID、动作、序号与参数（指南 8.5）。</summary>
    public readonly struct InteractionRequest
    {
        public readonly string EntityId;
        public readonly InteractionAction Action;
        public readonly int ClientSequence;
        public readonly int Argument;

        public InteractionRequest(string entityId, InteractionAction action, int clientSequence, int argument = 0)
        {
            EntityId = entityId;
            Action = action;
            ClientSequence = clientSequence;
            Argument = argument;
        }
    }

    public enum Map01RunOutcome
    {
        InProgress,
        Rescued,
        Died
    }

    /// <summary>
    /// Map01 单机版的权威层。单机时运行在本机，但写法按服务器权威：
    /// 种子与内容选择、物资与怪物生成、回收、商店、发电机、电池与撤离状态都只在这里改变，
    /// 玩家一侧只发 <see cref="InteractionRequest"/>，距离、状态、库存与序号在这里重新校验。
    /// 接入联网后，这个组件放到服务器上，请求改走 RPC，规则不需要重写。
    /// </summary>
    public sealed class Map01Authority : MonoBehaviour
    {
        /// <summary>测试或调试固定种子；为 null 时用 RunLootSeed。</summary>
        public static int? SeedOverride;
        /// <summary>测试用：不生成怪物（只验证地图与交互时避免战斗干扰）。</summary>
        public static bool SuppressMonsters;

        [Header("数据")]
        [SerializeField] private MapDefinition map;
        [SerializeField] private ContentTables tables;
        [SerializeField] private PlayerCharacter player;

        [Header("动态实体预制体")]
        [SerializeField] private LootContainer containerPrefab;
        [SerializeField] private MeleeEnemy monsterPrefab;

        [Header("物品")]
        [SerializeField] private ItemDefinition repairPart;
        [SerializeField] private ItemDefinition emptyBattery;
        [SerializeField] private ItemDefinition chargedBattery;
        [SerializeField] private ShopEntry[] shop = Array.Empty<ShopEntry>();

        [Header("规则")]
        [SerializeField, Min(0.5f)] private float interactRange = 2.5f;
        [SerializeField, Min(0.1f)] private float repairDuration = 4f;
        [SerializeField, Min(0.1f)] private float chargeDuration = 3f;
        [SerializeField, Min(1f)] private float secondsPerGeneratorCharge = 30f;
        [SerializeField, Min(1)] private int generatorMaxCharge = 2;
        [SerializeField, Min(1)] private int powerRequiredForRescue = 2;
        [SerializeField, Min(0.5f)] private float boardingDuration = 8f;
        [SerializeField, Min(1f)] private float extractionThreatRadius = 12f;
        [SerializeField, Min(1f)] private float generatorDiscoverRadius = 10f;
        [SerializeField, Min(1f)] private float patrolRadius = 14f;

        [Header("场景引用")]
        [SerializeField] private Renderer[] extractionMarkers = Array.Empty<Renderer>();
        [SerializeField] private Material extractionActiveMaterial;

        private readonly List<LootContainer> spawnedContainers = new List<LootContainer>();
        private readonly List<MeleeEnemy> spawnedMonsters = new List<MeleeEnemy>();
        private readonly Dictionary<string, int> purchases = new Dictionary<string, int>();
        private int lastSequence = -1;
        private float chargeElapsed;
        private bool charging;

        public event Action<string> Message;
        public event Action<Map01RunOutcome> RunEnded;

        public GeneratedContent Content { get; private set; }
        public ContentTables Tables => tables;
        public SafehouseLedger Ledger { get; private set; }
        public GeneratorModel Generator { get; private set; }
        public ExtractionModel Extraction { get; private set; }
        public Map01RunOutcome Outcome { get; private set; } = Map01RunOutcome.InProgress;
        public IReadOnlyList<ShopEntry> Shop => shop;
        public IReadOnlyList<LootContainer> Containers => spawnedContainers;
        public IReadOnlyList<MeleeEnemy> Monsters => spawnedMonsters;
        public PlayerCharacter Player => player;
        public bool IsCharging => charging;
        public float ChargeProgress => charging ? Mathf.Clamp01(chargeElapsed / chargeDuration) : 0f;
        public string LastMessage { get; private set; } = string.Empty;
        public float InteractRange => interactRange;
        public ItemDefinition EmptyBattery => emptyBattery;
        public ItemDefinition ChargedBattery => chargedBattery;
        public ItemDefinition RepairPart => repairPart;

        private Inventory Bag => player.Inventory.Inventory;

        private void Awake()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<PlayerCharacter>();
            }

            var seed = SeedOverride ?? RunLootSeed.Current;
            Content = ContentGenerator.Generate(map, tables, seed);
            Ledger = new SafehouseLedger(powerRequiredForRescue);
            Generator = new GeneratorModel(repairDuration, secondsPerGeneratorCharge, generatorMaxCharge);
            Extraction = new ExtractionModel(Content.ExtractionPointId, boardingDuration);
            SpawnLoot();
            SpawnMonsters();
        }

        private void Start()
        {
            player.Health.Died += OnPlayerDied;
            RefreshExtractionMarkers();
        }

        private void OnDestroy()
        {
            if (player != null)
            {
                player.Health.Died -= OnPlayerDied;
            }
        }

        // ---------- 请求 ----------

        public InteractionResult Request(InteractionRequest request)
        {
            var result = Validate(request, out var station);
            if (result == InteractionResult.Accepted)
            {
                lastSequence = request.ClientSequence;
                result = Execute(request, station);
            }

            if (result != InteractionResult.Accepted && result != InteractionResult.DuplicateSequence)
            {
                Notify(Describe(result));
            }

            return result;
        }

        private InteractionResult Validate(InteractionRequest request, out Map01Interactable station)
        {
            station = null;
            if (Outcome != Map01RunOutcome.InProgress) return InteractionResult.RunEnded;
            if (request.ClientSequence <= lastSequence) return InteractionResult.DuplicateSequence;
            station = Map01Interactable.Find(request.EntityId);
            if (station == null) return InteractionResult.UnknownEntity;
            if (station.HorizontalDistanceTo(player.transform.position) > interactRange + 0.5f) return InteractionResult.OutOfRange;
            var expected = request.Action switch
            {
                InteractionAction.Sell => Map01Station.RecycleTerminal,
                InteractionAction.Buy => Map01Station.ShopTerminal,
                InteractionAction.RepairGenerator => Map01Station.Generator,
                InteractionAction.ChargeBattery => Map01Station.Generator,
                _ => Map01Station.BatterySlot
            };
            return station.Station == expected ? InteractionResult.Accepted : InteractionResult.WrongStation;
        }

        private InteractionResult Execute(InteractionRequest request, Map01Interactable station)
        {
            switch (request.Action)
            {
                case InteractionAction.Sell:
                    return Sell();
                case InteractionAction.Buy:
                    return Buy(request.Argument);
                case InteractionAction.RepairGenerator:
                    if (Generator.State != GeneratorState.NeedsRepair && Generator.State != GeneratorState.Unknown && Generator.State != GeneratorState.Damaged)
                        return InteractionResult.InvalidState;
                    if (repairPart != null && Bag.CountOf(repairPart) == 0) return InteractionResult.MissingItem;
                    if (!Generator.BeginRepair()) return InteractionResult.InvalidState;
                    if (repairPart != null) Bag.Remove(repairPart, 1);
                    Notify("开始修理发电机，留在附近");
                    return InteractionResult.Accepted;
                case InteractionAction.ChargeBattery:
                    if (charging || Generator.StoredCharge <= 0) return InteractionResult.InvalidState;
                    if (Bag.CountOf(emptyBattery) == 0) return InteractionResult.MissingItem;
                    charging = true;
                    chargeElapsed = 0f;
                    Notify("正在给电池充电，留在附近");
                    return InteractionResult.Accepted;
                default:
                    if (Bag.CountOf(chargedBattery) == 0) return InteractionResult.MissingItem;
                    Bag.Remove(chargedBattery, 1);
                    Ledger.AddPower(1);
                    Notify($"安全屋电量 {Ledger.Power}/{Ledger.PowerRequiredForRescue}");
                    if (Ledger.RescueReady && Extraction.Activate())
                    {
                        RefreshExtractionMarkers();
                        Notify("救援已呼叫：撤离点已激活");
                    }

                    return InteractionResult.Accepted;
            }
        }

        private InteractionResult Sell()
        {
            var sold = 0;
            var value = 0;
            var kinds = new HashSet<ItemDefinition>();
            foreach (var stack in Bag.Stacks)
            {
                if (IsRecyclable(stack.Item)) kinds.Add(stack.Item);
            }

            foreach (var item in kinds)
            {
                var count = Bag.Remove(item, Bag.CountOf(item));
                sold += count;
                value += count * item.stashValue;
            }

            if (sold == 0) return InteractionResult.MissingItem;
            Ledger.AddCredits(value);
            Notify($"回收 {sold} 件，获得 {value} 价值");
            return InteractionResult.Accepted;
        }

        private InteractionResult Buy(int index)
        {
            if (index < 0 || index >= shop.Length) return InteractionResult.UnknownEntity;
            var entry = shop[index];
            purchases.TryGetValue(entry.entryId, out var bought);
            if (entry.purchaseLimit > 0 && bought >= entry.purchaseLimit) return InteractionResult.LimitReached;
            if (Ledger.Credits < entry.price) return InteractionResult.NotEnoughCredits;
            if (entry.kind == ShopKind.Item && Bag.FreeSlots < entry.item.size && !CanStack(entry.item)) return InteractionResult.InventoryFull;

            Ledger.TrySpend(entry.price);
            if (entry.kind == ShopKind.BackpackUpgrade)
            {
                Bag.Expand(entry.extraSlots);
            }
            else
            {
                Bag.TryAdd(entry.item);
            }

            purchases[entry.entryId] = bought + 1;
            Notify($"购买：{entry.displayName}");
            return InteractionResult.Accepted;
        }

        private bool CanStack(ItemDefinition item)
        {
            if (!item.IsStackable) return false;
            foreach (var stack in Bag.Stacks)
            {
                if (stack.Item == item && stack.Quantity < item.maxStack) return true;
            }

            return false;
        }

        /// <summary>回收物：材料与贵重品；电池和维修零件留给玩法使用，不能卖。</summary>
        public bool IsRecyclable(ItemDefinition item)
        {
            if (item == null || item == emptyBattery || item == chargedBattery || item == repairPart) return false;
            return item.category == ItemCategory.Material || item.category == ItemCategory.Valuable;
        }

        // ---------- 推进 ----------

        private void Update()
        {
            if (Outcome != Map01RunOutcome.InProgress)
            {
                return;
            }

            var dt = Time.deltaTime;
            var generatorStation = FindStation(Map01Station.Generator);
            var nearGenerator = generatorStation != null && generatorStation.HorizontalDistanceTo(player.transform.position) <= interactRange + 0.5f;
            if (Generator.State == GeneratorState.Unknown && generatorStation != null
                && generatorStation.HorizontalDistanceTo(player.transform.position) <= generatorDiscoverRadius)
            {
                Generator.Discover();
                Notify("发现发电机：需要机械零件修理");
            }

            var wasRepairing = Generator.IsRepairing;
            if (Generator.Tick(dt, nearGenerator))
            {
                Notify("发电机修好了，开始产电");
            }
            else if (wasRepairing && !Generator.IsRepairing)
            {
                Notify("离开发电机，修理中断（零件已消耗）");
            }

            if (charging)
            {
                if (!nearGenerator)
                {
                    charging = false;
                    Notify("离开发电机，充电中断");
                }
                else
                {
                    chargeElapsed += dt;
                    if (chargeElapsed >= chargeDuration && Generator.TryTakeCharge())
                    {
                        charging = false;
                        Bag.Remove(emptyBattery, 1);
                        Bag.TryAdd(chargedBattery);
                        Notify("电池充好了，带回安全屋电池槽");
                    }
                }
            }

            UpdateExtraction(dt);
        }

        private void UpdateExtraction(float dt)
        {
            if (!Extraction.IsUsable)
            {
                return;
            }

            var point = map.Find(Extraction.ChosenPointId);
            var inside = Horizontal(player.transform.position, point.position) <= point.radius;
            var threat = false;
            foreach (var monster in spawnedMonsters)
            {
                if (monster != null && !monster.Health.IsDead && Horizontal(monster.transform.position, point.position) <= extractionThreatRadius)
                {
                    threat = true;
                    break;
                }
            }

            var before = Extraction.State;
            var state = Extraction.Tick(dt, inside, threat);
            if (state == before)
            {
                return;
            }

            switch (state)
            {
                case ExtractionState.Boarding:
                    Notify($"登机中，坚持 {Extraction.BoardingDuration:F0} 秒");
                    break;
                case ExtractionState.Interrupted:
                    Notify(Extraction.LastInterrupt == ExtractionInterruptReason.MonsterNearby ? "登机中断：怪物靠近撤离点" : "登机中断：离开了撤离区");
                    break;
                case ExtractionState.Completed:
                    RunSettlement.Extract(Bag, SessionStash.Current);
                    End(Map01RunOutcome.Rescued, "救援成功");
                    break;
            }
        }

        private void OnPlayerDied()
        {
            End(Map01RunOutcome.Died, "你倒下了，背包物品丢失");
        }

        private void End(Map01RunOutcome outcome, string message)
        {
            if (Outcome != Map01RunOutcome.InProgress)
            {
                return;
            }

            Outcome = outcome;
            charging = false;
            player.SetControlEnabled(false);
            Notify(message);
            RunEnded?.Invoke(outcome);
        }

        // ---------- 生成 ----------

        private void SpawnLoot()
        {
            if (containerPrefab == null)
            {
                return;
            }

            var byPoint = new Dictionary<string, List<ItemDefinition>>();
            foreach (var loot in Content.Loot)
            {
                var item = tables.loot.Find(e => e.itemId == loot.ItemId)?.item;
                if (item == null) continue;
                if (!byPoint.TryGetValue(loot.PointId, out var list)) byPoint[loot.PointId] = list = new List<ItemDefinition>();
                list.Add(item);
            }

            var parent = new GameObject("Dynamic_Loot").transform;
            foreach (var pair in byPoint)
            {
                var point = map.Find(pair.Key);
                var container = Instantiate(containerPrefab, point.position, Quaternion.identity, parent);
                container.name = $"Loot_{pair.Key}";
                var heavy = point.HasTag("two_hand");
                container.InitializeWithItems(heavy ? "重型货箱" : "物资", heavy ? ContainerType.Toolbox : ContainerType.CivilianCrate,
                    point.depthTier >= 2 ? 2.5f : 1.5f, pair.Value);
                spawnedContainers.Add(container);
            }
        }

        private void SpawnMonsters()
        {
            if (monsterPrefab == null || SuppressMonsters)
            {
                return;
            }

            var parent = new GameObject("Dynamic_Monsters").transform;
            foreach (var spawn in Content.Monsters)
            {
                var point = map.Find(spawn.PointId);
                if (!NavMesh.SamplePosition(point.position, out var hit, 3f, NavMesh.AllAreas)) continue;
                var monster = Instantiate(monsterPrefab, hit.position, Quaternion.identity, parent);
                monster.name = $"{spawn.Behavior}_{spawn.PointId}";
                monster.SetPatrolRoute(BuildRoute(monster.transform, spawn, hit.position));
                spawnedMonsters.Add(monster);
            }
        }

        private Transform[] BuildRoute(Transform monster, MonsterSpawn spawn, Vector3 origin)
        {
            // 驻守与骚扰类原地待命，巡逻类在出生点周围绕行。
            if (spawn.Behavior != MonsterBehavior.Patrol)
            {
                return Array.Empty<Transform>();
            }

            var route = new GameObject($"{monster.name}_Route").transform;
            route.SetParent(monster.parent, false);
            var points = new List<Transform>();
            for (var i = 0; i < 4; i++)
            {
                var angle = i * Mathf.PI * 0.5f + StableAngle(spawn.PointId);
                var target = origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * patrolRadius;
                if (!NavMesh.SamplePosition(target, out var hit, 4f, NavMesh.AllAreas)) continue;
                var node = new GameObject($"Patrol_{i}").transform;
                node.SetParent(route, false);
                node.position = hit.position;
                points.Add(node);
            }

            return points.ToArray();
        }

        // ---------- 工具 ----------

        public Map01Interactable FindStation(Map01Station station)
        {
            foreach (var candidate in Map01Interactable.Active)
            {
                if (candidate.Station == station) return candidate;
            }

            return null;
        }

        private void RefreshExtractionMarkers()
        {
            foreach (var marker in extractionMarkers)
            {
                if (marker == null) continue;
                var active = Extraction.IsUsable && marker.name.EndsWith(Extraction.ChosenPointId);
                marker.gameObject.SetActive(!Extraction.IsUsable || active);
                if (active && extractionActiveMaterial != null) marker.sharedMaterial = extractionActiveMaterial;
            }
        }

        private void Notify(string text)
        {
            LastMessage = text;
            Message?.Invoke(text);
        }

        private static string Describe(InteractionResult result) => result switch
        {
            InteractionResult.UnknownEntity => "无效的交互对象",
            InteractionResult.OutOfRange => "距离太远",
            InteractionResult.WrongStation => "这里不能这样做",
            InteractionResult.NotEnoughCredits => "价值不足",
            InteractionResult.InventoryFull => "背包已满",
            InteractionResult.MissingItem => "缺少需要的物品",
            InteractionResult.InvalidState => "现在不能这样做",
            InteractionResult.LimitReached => "已达购买上限",
            InteractionResult.RunEnded => "本局已结束",
            _ => string.Empty
        };

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static float StableAngle(string id) => (ContentGenerator.StableHash(id) & 0xffff) / 65535f * Mathf.PI * 2f;
    }
}
