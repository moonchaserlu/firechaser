using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WildernessExtraction.Map01;

namespace WildernessExtraction.Tutorial
{
    /// <summary>
    /// 教学导演：把玩家已有的行为（走路、进光、搜箱、开背包、合成、建火、翻滚、击杀、射击、换弹）翻译成事实交给
    /// <see cref="TutorialStepModel"/>；段落完成时点亮对应的引路灯。
    /// 教学里不会真正死亡：黑暗伤害有生命底线，被怪物打到底线以下时倒下，在最近一盏已点亮的灯旁满血醒来。
    /// 走完或从暂停里跳过后，把当前存档改记为 Map01 并进入 Map01。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialDirector : MonoBehaviour
    {
        /// <summary>测试用：结束教学时不切场景，只记下要去的场景。</summary>
        public static bool SuppressSceneLoad;

        [SerializeField] private TutorialConfig config;
        [SerializeField] private Map01Authority authority;
        [SerializeField] private Map01RunFlow flow;
        [Tooltip("合成教学要做出的物品（燃料包）。")]
        [SerializeField] private ItemDefinition fuelItem;
        [Tooltip("射击教学拾取的手枪。")]
        [SerializeField] private ItemDefinition pistolItem;
        [Tooltip("合成燃料包的材料，箱子被搜空后仍缺时补到背包里。")]
        [SerializeField] private CraftingRecipeBook recipes;

        private readonly List<TutorialBeacon> beacons = new List<TutorialBeacon>();
        private readonly List<TutorialZone> zones = new List<TutorialZone>();
        private readonly List<TutorialEnemy> enemies = new List<TutorialEnemy>();
        private readonly List<TutorialTarget> targets = new List<TutorialTarget>();
        private TutorialZone exitZone;
        private TutorialRockfall rockfall;
        private TutorialBreachWall breachWall;
        private TutorialBeacon lastLit;
        private bool skipped;
        private PlayerCharacter player;
        private Vector3 lastPosition;
        private float walked;
        private float exitHeld;
        private float lastProgressTime;
        private float noFuelSince = -1f;
        private int knockdowns;
        private int lastAnnouncedKnockdown;

        public TutorialStepModel Model { get; private set; }
        public TutorialConfig Config => config;
        public PlayerCharacter Player => player;
        public bool IsEnding { get; private set; }
        /// <summary>结束时去往的场景（SuppressSceneLoad 时供测试检查）。</summary>
        public string LoadedScene { get; private set; }
        /// <summary>门前已停留的秒数。</summary>
        public float ExitHeld => exitHeld;
        public float ExitHoldSeconds => config.exitHoldSeconds;
        public int Knockdowns => knockdowns;
        public TutorialBreachWall BreachWall => breachWall;
        public float RealtimeStarted { get; private set; }
        /// <summary>需要重新提醒：上次提醒后这么久没有进展。HUD 用它让提示条闪烁。</summary>
        public float IdleHintStartedRealtime { get; private set; } = -100f;

        public event Action<string> Notice;

        public TutorialStep CurrentStep => Model == null || Model.IsFinished ? null : config.steps[Model.CurrentIndex];

        private void Awake()
        {
            if (authority == null) authority = FindAnyObjectByType<Map01Authority>();
            if (flow == null) flow = FindAnyObjectByType<Map01RunFlow>();
            player = authority.Player;
            var steps = new List<(string, string[])>();
            foreach (var step in config.steps) steps.Add((step.stepId, step.requiredFacts));
            Model = new TutorialStepModel(steps);
            Model.StepCompleted += OnStepCompleted;
            Model.Finished += OnFinished;
        }

        private void Start()
        {
            beacons.AddRange(FindObjectsByType<TutorialBeacon>(FindObjectsInactive.Include));
            zones.AddRange(FindObjectsByType<TutorialZone>(FindObjectsInactive.Include));
            enemies.AddRange(FindObjectsByType<TutorialEnemy>(FindObjectsInactive.Include));
            targets.AddRange(FindObjectsByType<TutorialTarget>(FindObjectsInactive.Include));
            exitZone = zones.Find(z => z.Fact == TutorialFacts.AtExit);
            zones.Remove(exitZone);
            rockfall = FindAnyObjectByType<TutorialRockfall>(FindObjectsInactive.Include);
            breachWall = FindAnyObjectByType<TutorialBreachWall>(FindObjectsInactive.Include);
            if (breachWall != null)
            {
                breachWall.Armed += () => Report(TutorialFacts.ChargePlaced);
                breachWall.Blown += () => Report(TutorialFacts.WallBlown);
            }

            player.Health.Damaged += OnPlayerDamaged;
            player.Interactor.ItemsCollected += OnItemsCollected;
            player.Motor.DodgeStarted += OnDodge;
            player.Shooter.ReloadStarted += () => Report(TutorialFacts.Reloaded);
            if (player.Placer != null)
            {
                player.Placer.Built += _ => Report(TutorialFacts.CampfireBuilt);
            }

            if (flow != null) flow.BackpackOpened += () => Report(TutorialFacts.BackpackOpened);
            foreach (var enemy in enemies)
            {
                var tagged = enemy;
                tagged.Health.Died += () => Report(tagged.KillFact);
                tagged.Enemy.Attacked += () =>
                {
                    if (tagged.Pinned) Report(TutorialFacts.EnemyAttacking);
                };
            }

            foreach (var target in targets)
            {
                target.Health.Died += OnTargetBroken;
            }

            // 开局只有短刀：手枪槽（1）还是空的，先拿着短刀。
            if (pistolItem != null && Bag.CountOf(pistolItem) == 0) player.Shooter.TrySelectSlot(1);

            lastPosition = player.transform.position;
            RealtimeStarted = Time.realtimeSinceStartup;
            lastProgressTime = Time.time;
        }

        private void OnDestroy()
        {
            if (player == null) return;
            player.Health.Damaged -= OnPlayerDamaged;
            player.Interactor.ItemsCollected -= OnItemsCollected;
            player.Motor.DodgeStarted -= OnDodge;
        }

        private void Update()
        {
            if (Model.IsFinished || authority.Outcome != Map01RunOutcome.InProgress) return;
            var position = player.transform.position;
            var step = Vector3.ProjectOnPlane(position - lastPosition, Vector3.up).magnitude;
            lastPosition = position;
            if (step < 2f) walked += step;
            if (walked >= config.moveDistance) Report(TutorialFacts.Moved);
            if (player.Motor.IsSprinting && player.Input.Move.sqrMagnitude > 0.01f) Report(TutorialFacts.Sprinted);
            if (player.Exposure != null && player.Exposure.State != LightExposureState.Safe) Report(TutorialFacts.FeltWarning);
            if (player.Placer != null && player.Placer.State != CampfirePlacerState.Idle) Report(TutorialFacts.CampfirePreview);
            if (fuelItem != null && Bag.CountOf(fuelItem) > 0) Report(TutorialFacts.HasFuel);
            if (pistolItem != null && Bag.CountOf(pistolItem) > 0) Report(TutorialFacts.PistolTaken);
            if (breachWall != null && breachWall.Explosive != null && Bag.CountOf(breachWall.Explosive) > 0) Report(TutorialFacts.ExplosiveTaken);
            if (breachWall != null && player.Input.InteractPressed && player.Interactor.Focus == null && !player.Interactor.IsSearching)
            {
                breachWall.TryArm(player);
            }

            if (Model.HasFact(TutorialFacts.PistolTaken) && player.Shooter.ActiveSlot == 0 && player.Shooter.Weapon.AmmoInMagazine == 0)
            {
                Report(TutorialFacts.MagazineEmpty);
            }

            foreach (var zone in zones)
            {
                if (!Model.HasFact(zone.Fact) && zone.Contains(position)) Report(zone.Fact);
            }

            UpdateExit(position);
            if (rockfall != null && !rockfall.HasFallen && Model.HasFact(TutorialFacts.AtExit))
            {
                rockfall.Fall();
                Say("身后轰的一声，落石把来路堵死了");
            }

            UpdateFallbacks();

            if (Time.time - lastProgressTime >= config.idleHintSeconds)
            {
                lastProgressTime = Time.time;
                IdleHintStartedRealtime = Time.realtimeSinceStartup;
            }
        }

        /// <summary>记下一个事实。只认 TutorialFacts 或场景组件上配置的名字。</summary>
        public void Report(string fact)
        {
            if (Model == null || string.IsNullOrEmpty(fact) || Model.HasFact(fact)) return;
            foreach (var beacon in beacons)
            {
                if (beacon.LitAfterFact == fact && !beacon.IsLit) Light(beacon);
            }

            Model.Report(fact);
            lastProgressTime = Time.time;
        }

        private void Light(TutorialBeacon beacon)
        {
            beacon.Light();
            lastLit = beacon;
        }

        /// <summary>当前应显示的提示：最后一条 afterFact 已到齐的那句。</summary>
        public string CurrentLine()
        {
            var step = CurrentStep;
            if (step == null) return string.Empty;
            var text = string.Empty;
            foreach (var line in step.lines)
            {
                if (string.IsNullOrEmpty(line.afterFact) || Model.HasFact(line.afterFact)) text = line.text;
            }

            return text;
        }

        /// <summary>暂停菜单里的“跳过教学”。</summary>
        public void Skip()
        {
            if (IsEnding) return;
            if (flow != null) flow.SetPaused(false);
            skipped = true;
            Model.Skip();
        }

        // ---------- 进度 ----------

        private void OnStepCompleted(int index)
        {
            var id = Model.IdAt(index);
            foreach (var beacon in beacons)
            {
                if (beacon.LitAfterStep == id && !beacon.IsLit) Light(beacon);
            }
        }

        private void OnFinished()
        {
            if (IsEnding) return;
            IsEnding = true;
            StartCoroutine(EndRoutine(!skipped));
        }

        private IEnumerator EndRoutine(bool showCaption)
        {
            player.SetControlEnabled(false);
            if (showCaption)
            {
                var until = Time.realtimeSinceStartup + config.endingSeconds;
                while (Time.realtimeSinceStartup < until) yield return null;
            }

            LoadedScene = config.nextScenePath;
            SaveSlots.SetActiveScene(config.nextScenePath);
            if (!SuppressSceneLoad) GameScenes.Load(config.nextScenePath);
        }

        private void UpdateExit(Vector3 position)
        {
            if (exitZone == null || Model.CurrentId == null) return;
            var inside = exitZone.Contains(position);
            if (inside) Report(TutorialFacts.AtExit);
            var active = Array.IndexOf(CurrentStep.requiredFacts, TutorialFacts.ExitHeld) >= 0;
            if (!active || !inside)
            {
                if (active && exitHeld > 0f) Say("离开了门前，重新站稳");
                exitHeld = 0f;
                return;
            }

            exitHeld += Time.deltaTime;
            if (exitHeld >= config.exitHoldSeconds) Report(TutorialFacts.ExitHeld);
        }

        // ---------- 兜底：不让玩家卡住 ----------

        private void UpdateFallbacks()
        {
            var id = Model.CurrentId;
            var step = CurrentStep;
            if (step == null) return;

            // 合成段：箱子都搜空了还缺材料（例如被丢掉），把差的补进背包。
            if (Array.IndexOf(step.requiredFacts, TutorialFacts.HasFuel) >= 0 && !Model.HasFact(TutorialFacts.HasFuel))
            {
                var recipe = FuelRecipe();
                if (recipe != null && !Crafting.HasIngredients(Bag, recipe) && !AnyCrateLeft())
                {
                    foreach (var ingredient in recipe.ingredients)
                    {
                        for (var have = Bag.CountOf(ingredient.item); have < ingredient.amount && Bag.TryAdd(ingredient.item); have++) { }
                    }

                    Say("你在箱底又摸到一些材料");
                }
            }

            // 炸墙段：炸药丢了，墙还没炸开，再给一份。
            if (breachWall != null && breachWall.State == TutorialBreachWall.WallState.Intact && Model.HasFact(TutorialFacts.ExplosiveTaken)
                && Bag.CountOf(breachWall.Explosive) == 0 && Bag.TryAdd(breachWall.Explosive))
            {
                Say("墙角还剩一捆炸药");
            }

            // 建火段：燃料用掉了、火也灭了还没走到对面，再给一份燃料。
            if (Array.IndexOf(step.requiredFacts, TutorialFacts.CampfireBuilt) >= 0 && id != null)
            {
                var stuck = fuelItem != null && Bag.CountOf(fuelItem) == 0 && Campfire.Burning.Count == 0 && player.Placer.State == CampfirePlacerState.Idle;
                if (!stuck)
                {
                    noFuelSince = -1f;
                }
                else if (noFuelSince < 0f)
                {
                    noFuelSince = Time.time;
                }
                else if (Time.time - noFuelSince > 3f)
                {
                    noFuelSince = -1f;
                    if (Bag.TryAdd(fuelItem)) Say("灯下还压着一份燃料包，再试一次");
                }
            }
        }

        private CraftingRecipe FuelRecipe()
        {
            if (recipes == null) return null;
            return recipes.recipes.Find(r => r.result == fuelItem);
        }

        private bool AnyCrateLeft()
        {
            foreach (var container in LootContainer.Active)
            {
                if (container.GetComponent<TutorialCrate>() != null && !container.IsEmpty) return true;
            }

            return false;
        }

        // ---------- 事件 ----------

        private void OnItemsCollected(LootContainer container, TransferResult result)
        {
            var crate = container != null ? container.GetComponent<TutorialCrate>() : null;
            if (crate != null) Report(crate.Fact);
        }

        private void OnDodge()
        {
            foreach (var enemy in enemies)
            {
                if (enemy.Pinned && !enemy.Health.IsDead
                    && Vector3.Distance(enemy.transform.position, player.transform.position) <= 4f)
                {
                    Report(TutorialFacts.Dodged);
                    return;
                }
            }
        }

        private void OnTargetBroken()
        {
            Report(TutorialFacts.TargetHit);
            foreach (var target in targets)
            {
                if (!target.IsBroken) return;
            }

            Report(TutorialFacts.TargetsCleared);
        }

        private void OnPlayerDamaged(float amount)
        {
            var health = player.Health;
            if (health.IsDead || IsEnding) return;
            if (player.Exposure != null && player.Exposure.IsApplyingDamage)
            {
                if (health.Current < config.darknessHealthFloor) health.Heal(config.darknessHealthFloor - health.Current);
                return;
            }

            if (exitHeld > 0f)
            {
                exitHeld = 0f;
                Say("受伤了，门前停留中断");
            }

            if (health.Current < config.knockdownHealth) Knockdown();
        }

        private void Knockdown()
        {
            knockdowns++;
            var checkpoint = LatestCheckpoint();
            player.Interactor.CancelSearch(SearchCancelReason.Damaged);
            player.Motor.Teleport(checkpoint);
            lastPosition = checkpoint;
            player.Health.Heal(player.Health.Max);
            Say("你倒下了……在最近一盏亮着的灯旁醒来");

            if (knockdowns - lastAnnouncedKnockdown >= config.knockdownsBeforeEasing)
            {
                var eased = enemies.Find(e => e.Easeable && !e.Health.IsDead && e.gameObject.activeSelf);
                if (eased != null)
                {
                    lastAnnouncedKnockdown = knockdowns;
                    eased.gameObject.SetActive(false);
                    Say("远处一只失火者走进了黑暗");
                }
            }
        }

        private Vector3 LatestCheckpoint()
        {
            // 落石之后来路已断，只在庇护所一侧醒来。
            if (rockfall != null && rockfall.HasFallen) return rockfall.SafeSidePosition;

            // 最近一盏被点亮的灯；还没点过灯时用开局就亮的那盏。
            if (lastLit != null) return lastLit.RespawnPosition;
            var start = beacons.Find(b => b.IsLit);
            return start != null ? start.RespawnPosition : player.transform.position;
        }

        private void Say(string text)
        {
            authority.Announce(text);
            Notice?.Invoke(text);
        }

        private Inventory Bag => player.Inventory.Inventory;
    }
}
