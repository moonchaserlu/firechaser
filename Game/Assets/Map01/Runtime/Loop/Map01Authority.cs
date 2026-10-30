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
        DepositBattery,
        Sacrifice,
        /// <summary>任务交互：炉火、剧情记录、旧地图，由任务导演判定。</summary>
        Quest,
        /// <summary>工作台合成；参数为配方序号。</summary>
        Craft
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
        RunEnded,
        /// <summary>配方还没解锁（任务模式下对应任务未完成）。</summary>
        Locked
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
        /// <summary>测试用：覆盖场景里的任务模式开关；为 null 时用场景配置。</summary>
        public static bool? QuestModeOverride;

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
        [Tooltip("别人的身份牌：在山顶祭坛献祭后地图变为白天。")]
        [SerializeField] private ItemDefinition identityTag;
        [SerializeField] private ShopEntry[] shop = Array.Empty<ShopEntry>();
        [Tooltip("庇护所工作台的配方。")]
        [SerializeField] private CraftingRecipeBook recipes;

        [Header("光照")]
        [SerializeField] private Map01Daylight daylight;
        [Tooltip("厂房内的灯，发电机修好后点亮。")]
        [SerializeField] private SafeLightSource factoryLamp;
        [Tooltip("撤离点的灯，名称以撤离点 ID 结尾；本局撤离点激活时点亮。")]
        [SerializeField] private SafeLightSource[] extractionLamps = Array.Empty<SafeLightSource>();

        [Header("任务模式")]
        [Tooltip("开启后按 Q01–Q10 任务链游玩：第一块充好的电池用于恢复庇护所照明，不计入救援电量。关闭则为原救援循环。")]
        [SerializeField] private bool questMode;
        [SerializeField] private Map01QuestDirector questDirector;

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
        /// <summary>任务模式下第一块电池装进庇护所供电槽：庇护所恢复照明。</summary>
        public event Action SafehousePowered;

        public GeneratedContent Content { get; private set; }
        public ContentTables Tables => tables;
        public SafehouseLedger Ledger { get; private set; }
        public GeneratorModel Generator { get; private set; }
        public ExtractionModel Extraction { get; private set; }
        public Map01RunOutcome Outcome { get; private set; } = Map01RunOutcome.InProgress;
        /// <summary>结算结果：救援成功时为带出的物品（已存入仓库），死亡时为丢失的物品。</summary>
        public RunResult Result { get; private set; }
        public IReadOnlyList<ShopEntry> Shop => shop;
        public IReadOnlyList<CraftingRecipe> Recipes => recipes != null ? recipes.recipes : (IReadOnlyList<CraftingRecipe>)Array.Empty<CraftingRecipe>();
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
        public ItemDefinition IdentityTag => identityTag;
        public Map01Daylight Daylight => daylight;
        public bool QuestMode => QuestModeOverride ?? questMode;
        /// <summary>任务模式：庇护所照明是否已由电池恢复。</summary>
        public bool SafehouseLit { get; private set; }
        public LootContainer ContainerPrefab => containerPrefab;
        public Map01QuestDirector QuestDirector => questDirector;

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
            if (RaidTransfer.Active)
            {
                ApplyRaidLoadout();
            }
        }

        /// <summary>
        /// 从地下安全屋出发：背包换成出发时选好的物资；小屋改作前哨与登陆点，
        /// 回收、商店和工作台都在安全屋里，这里的对应设施隐藏。
        /// </summary>
        private void ApplyRaidLoadout()
        {
            Bag.TakeAll();
            foreach (var item in RaidTransfer.Loadout)
            {
                if (item != null && !Bag.TryAdd(item)) Debug.LogWarning($"[Map01] 出发物资放不下：{item.displayName}");
            }

            foreach (var station in new List<Map01Interactable>(Map01Interactable.Active))
            {
                if (station.Station == Map01Station.RecycleTerminal || station.Station == Map01Station.ShopTerminal || station.Station == Map01Station.Workbench)
                {
                    station.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>从安全屋出发的这一局：是否可以经前哨返回安全屋。</summary>
        public bool CanReturnToShelter => RaidTransfer.Active && Outcome == Map01RunOutcome.InProgress;

        /// <summary>在前哨登陆点停留满时长：带着背包回到地下安全屋。</summary>
        public void ReturnToShelter()
        {
            if (!CanReturnToShelter) return;
            Result = RunSettlement.Extract(Bag, new Stash());
            RaidTransfer.Finish(RaidOutcome.Returned, Result.Items);
            End(Map01RunOutcome.Rescued, "回到前哨，下到地下庇护所");
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
            var executed = result == InteractionResult.Accepted;
            if (executed)
            {
                lastSequence = request.ClientSequence;
                result = Execute(request, station);
            }

            // 任务交互的失败原因由任务导演自己给出更具体的提示。
            var questHandled = executed && request.Action == InteractionAction.Quest;
            if (result != InteractionResult.Accepted && result != InteractionResult.DuplicateSequence && !questHandled)
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
                InteractionAction.Sacrifice => Map01Station.Altar,
                InteractionAction.Craft => Map01Station.Workbench,
                InteractionAction.Quest => station.Station,
                _ => Map01Station.BatterySlot
            };
            if (request.Action == InteractionAction.Quest && !Map01Interactable.IsQuestStation(station.Station)) return InteractionResult.WrongStation;
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
                case InteractionAction.Craft:
                    return Craft(request.Argument);
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
                case InteractionAction.Sacrifice:
                    if (daylight == null || daylight.IsDay) return InteractionResult.InvalidState;
                    if (identityTag == null || Bag.CountOf(identityTag) == 0) return InteractionResult.MissingItem;
                    Bag.Remove(identityTag, 1);
                    daylight.BeginDay();
                    Notify("身份牌燃尽，天亮了：黑暗不再伤人");
                    return InteractionResult.Accepted;
                case InteractionAction.Quest:
                    return questDirector != null && QuestMode ? questDirector.Interact(station) : InteractionResult.InvalidState;
                default:
                    if (Bag.CountOf(chargedBattery) == 0) return InteractionResult.MissingItem;
                    Bag.Remove(chargedBattery, 1);
                    if (QuestMode && !SafehouseLit)
                    {
                        // 任务模式：第一块电池只用于恢复庇护所照明，不计入救援电量，也不呼叫救援。
                        SafehouseLit = true;
                        Notify("电池装好了：庇护所的灯亮了");
                        SafehousePowered?.Invoke();
                        return InteractionResult.Accepted;
                    }

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

        private InteractionResult Craft(int index)
        {
            var list = Recipes;
            if (index < 0 || index >= list.Count) return InteractionResult.UnknownEntity;
            var recipe = list[index];
            if (!IsRecipeUnlocked(recipe)) return InteractionResult.Locked;
            switch (Crafting.TryCraft(Bag, recipe))
            {
                case CraftResult.Crafted:
                    Notify(recipe.resultAmount > 1 ? $"合成：{recipe.result.displayName} ×{recipe.resultAmount}" : $"合成：{recipe.result.displayName}");
                    return InteractionResult.Accepted;
                case CraftResult.MissingIngredients:
                    return InteractionResult.MissingItem;
                case CraftResult.InventoryFull:
                    return InteractionResult.InventoryFull;
                default:
                    return InteractionResult.InvalidState;
            }
        }

        /// <summary>任务模式下，配方在 unlockKey 对应的任务完成后解锁；原救援循环里全部可用。</summary>
        public bool IsRecipeUnlocked(CraftingRecipe recipe)
        {
            if (recipe == null) return false;
            if (string.IsNullOrEmpty(recipe.unlockKey) || !QuestMode || questDirector == null || !questDirector.IsRunning) return true;
            return questDirector.IsCompleted(recipe.unlockKey);
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
            if (item == null || item == emptyBattery || item == chargedBattery || item == repairPart || item == identityTag) return false;
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
                if (factoryLamp != null) factoryLamp.SetLit(true);
                Notify("发电机修好了，厂房亮灯并开始产电");
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
                    if (RaidTransfer.Active)
                    {
                        // 从安全屋出发：救援把人和背包一起送回地下安全屋，物资入安全屋仓库而不是会话仓库。
                        Result = RunSettlement.Extract(Bag, new Stash());
                        RaidTransfer.Finish(RaidOutcome.Returned, Result.Items);
                    }
                    else
                    {
                        Result = RunSettlement.Extract(Bag, SessionStash.Current);
                    }

                    End(Map01RunOutcome.Rescued, "救援成功");
                    break;
            }
        }

        private void OnPlayerDied()
        {
            if (Outcome == Map01RunOutcome.InProgress)
            {
                Result = RunSettlement.Die(Bag);
                RaidTransfer.Finish(RaidOutcome.Died, null);
            }

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

            foreach (var lamp in extractionLamps)
            {
                if (lamp != null) lamp.SetLit(Extraction.IsUsable && lamp.name.EndsWith(Extraction.ChosenPointId));
            }
        }

        /// <summary>向 HUD 发一条提示（任务导演也用它）。</summary>
        public void Announce(string text) => Notify(text);

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
            InteractionResult.Locked => "这个配方还没学会",
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
