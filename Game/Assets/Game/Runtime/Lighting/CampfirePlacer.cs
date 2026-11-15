using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace WildernessExtraction
{
    public enum CampfirePlacerState
    {
        Idle,
        Previewing,
        Building,
    }

    public enum CampfireBuildCancelReason
    {
        Cancelled,
        Moved,
        Damaged,
        OutOfRange,
        NoFuel,
    }

    /// <summary>
    /// 建火：Q 进入预览，鼠标选择地面，左键确认后原地建造；右键、Esc 或再按 Q 取消。
    /// 建造完成时才扣燃料；建造中移动、受到攻击或离开放置范围会取消且不扣燃料。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CampfirePlacer : MonoBehaviour
    {
        private const int CircleSegments = 48;
        private const float CircleHeight = 0.06f;

        [SerializeField] private CampfireConfig config;
        [SerializeField] private Campfire campfirePrefab;
        [Tooltip("预览：火堆占地圈。")]
        [SerializeField] private LineRenderer previewFootprint;
        [Tooltip("预览：点燃后的安全光范围圈。")]
        [SerializeField] private LineRenderer previewRange;
        [SerializeField] private Material validMaterial;
        [SerializeField] private Material invalidMaterial;

        private readonly Collider[] overlapBuffer = new Collider[16];
        private readonly List<Vector3> firePositions = new List<Vector3>();
        private readonly List<Vector3> corePositions = new List<Vector3>();
        private readonly TimedProgress build = new TimedProgress();

        private PlayerInputReader input;
        private PlayerMotor motor;
        private PlayerInventory inventory;
        private Health health;
        private LightExposure exposure;
        private Vector3 buildStartPosition;
        private int cancelFrame = -1;

        public event Action<CampfirePlacementResult> PlacementRejected;
        public event Action<CampfireBuildCancelReason> BuildCancelled;
        public event Action<Campfire> Built;

        public CampfirePlacerState State { get; private set; }
        public Vector3 Site { get; private set; }
        public CampfirePlacementResult SiteResult { get; private set; }
        public float BuildProgress => build.Progress;
        public CampfireConfig Config => config;
        public int FuelCount => config != null && config.fuelItem != null ? inventory.Inventory.CountOf(config.fuelItem) : 0;

        /// <summary>预览/建造中，或本帧刚取消：Esc 不应同时触发暂停。</summary>
        public bool ConsumesCancelThisFrame => State != CampfirePlacerState.Idle || cancelFrame == Time.frameCount;

        private void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            motor = GetComponent<PlayerMotor>();
            inventory = GetComponent<PlayerInventory>();
            health = GetComponent<Health>();
            exposure = GetComponent<LightExposure>();
            ShowPreview(false);
        }

        private void OnEnable()
        {
            health.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            health.Damaged -= OnDamaged;
            if (State != CampfirePlacerState.Idle) Cancel(CampfireBuildCancelReason.Cancelled);
            input.AttackBlocked = false;
        }

        private void Update()
        {
            switch (State)
            {
                case CampfirePlacerState.Idle:
                    // 确认放置的左键松开前不恢复射击，避免点击后立刻开枪。
                    if (input.AttackBlocked && !input.AttackButtonDown) input.AttackBlocked = false;
                    if (input.BuildFirePressed) BeginPreview();
                    break;
                case CampfirePlacerState.Previewing:
                    UpdatePreview();
                    break;
                case CampfirePlacerState.Building:
                    UpdateBuilding();
                    break;
            }
        }

        /// <summary>进入预览。燃料不足时拒绝并返回 false。</summary>
        public bool BeginPreview()
        {
            if (State != CampfirePlacerState.Idle || !isActiveAndEnabled) return false;
            if (FuelCount < config.fuelCost)
            {
                PlacementRejected?.Invoke(CampfirePlacementResult.NoFuel);
                return false;
            }

            State = CampfirePlacerState.Previewing;
            input.AttackBlocked = true;
            SetSite(motor.AimPoint);
            ShowPreview(true);
            return true;
        }

        /// <summary>在指定地点开始建造（左键确认或测试调用）。地点无效时拒绝。</summary>
        public bool TryConfirm(Vector3 site)
        {
            if (State != CampfirePlacerState.Previewing) return false;
            SetSite(site);
            if (SiteResult != CampfirePlacementResult.Valid)
            {
                PlacementRejected?.Invoke(SiteResult);
                return false;
            }

            State = CampfirePlacerState.Building;
            buildStartPosition = transform.position;
            build.Start(config.buildDuration);
            DrawPreview(true);
            return true;
        }

        public void Cancel(CampfireBuildCancelReason reason)
        {
            if (State == CampfirePlacerState.Idle) return;
            State = CampfirePlacerState.Idle;
            build.Cancel();
            ShowPreview(false);
            cancelFrame = Time.frameCount;
            BuildCancelled?.Invoke(reason);
        }

        /// <summary>评估某点能否建火（不改变状态）。</summary>
        public CampfirePlacementResult Evaluate(Vector3 site)
        {
            var onGround = NavMesh.SamplePosition(site, out var hit, config.groundTolerance, NavMesh.AllAreas)
                           && CampfirePlacementRules.HorizontalDistance(hit.position, site) <= config.groundTolerance;

            firePositions.Clear();
            foreach (var fire in Campfire.Burning) firePositions.Add(fire.transform.position);
            corePositions.Clear();
            foreach (var area in LightRegistry.Scene.Areas)
            {
                if (area is SafeLightSource source && source.Kind == SafeLightKind.SafeHouse) corePositions.Add(source.transform.position);
            }

            return CampfirePlacementRules.Evaluate(transform.position, site, FuelCount, config.fuelCost, config.placeRange,
                config.footprintRadius, onGround, IsBlocked(site), firePositions, corePositions, config.safeHouseCoreRadius);
        }

        private void UpdatePreview()
        {
            if (input.CancelActionPressed || input.PausePressed || input.BuildFirePressed)
            {
                Cancel(CampfireBuildCancelReason.Cancelled);
                return;
            }

            SetSite(motor.AimPoint);
            if (input.AttackPressedThisFrame)
            {
                TryConfirm(Site);
            }
        }

        private void UpdateBuilding()
        {
            if (input.CancelActionPressed || input.PausePressed)
            {
                Cancel(CampfireBuildCancelReason.Cancelled);
                return;
            }

            if (input.Move.sqrMagnitude > 0.01f || motor.IsDodging
                || CampfirePlacementRules.HorizontalDistance(transform.position, buildStartPosition) > config.moveCancelDistance)
            {
                Cancel(CampfireBuildCancelReason.Moved);
                return;
            }

            if (CampfirePlacementRules.HorizontalDistance(transform.position, Site) > config.placeRange + 0.01f)
            {
                Cancel(CampfireBuildCancelReason.OutOfRange);
                return;
            }

            if (!build.Tick(Time.deltaTime)) return;

            if (inventory.Inventory.Remove(config.fuelItem, config.fuelCost) < config.fuelCost)
            {
                Cancel(CampfireBuildCancelReason.NoFuel);
                return;
            }

            State = CampfirePlacerState.Idle;
            ShowPreview(false);
            var fire = Instantiate(campfirePrefab, Site, Quaternion.identity);
            Built?.Invoke(fire);
        }

        private void OnDamaged(float amount)
        {
            if (State != CampfirePlacerState.Building) return;
            if (!config.darknessDamageCancelsBuild && exposure != null && exposure.IsApplyingDamage) return;
            Cancel(CampfireBuildCancelReason.Damaged);
        }

        private void SetSite(Vector3 aim)
        {
            Site = CampfirePlacementRules.ClampToRange(transform.position, aim, config.placeRange);
            SiteResult = Evaluate(Site);
            DrawPreview(SiteResult == CampfirePlacementResult.Valid);
        }

        private bool IsBlocked(Vector3 site)
        {
            var center = site + Vector3.up * (config.footprintRadius + 0.1f);
            var count = Physics.OverlapSphereNonAlloc(center, config.footprintRadius, overlapBuffer, config.obstacleMask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                if (!overlapBuffer[i].transform.IsChildOf(transform)) return true;
            }

            return false;
        }

        private void ShowPreview(bool show)
        {
            if (previewFootprint != null) previewFootprint.enabled = show;
            if (previewRange != null) previewRange.enabled = show;
        }

        private void DrawPreview(bool valid)
        {
            DrawCircle(previewFootprint, config.footprintRadius, valid);
            DrawCircle(previewRange, config.lightRadius, valid);
        }

        private void DrawCircle(LineRenderer line, float radius, bool valid)
        {
            if (line == null) return;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = CircleSegments;
            line.sharedMaterial = valid ? validMaterial : invalidMaterial;
            for (var i = 0; i < CircleSegments; i++)
            {
                var angle = i * Mathf.PI * 2f / CircleSegments;
                line.SetPosition(i, Site + new Vector3(Mathf.Cos(angle) * radius, CircleHeight, Mathf.Sin(angle) * radius));
            }
        }
    }
}
