using System;
using UnityEngine;

namespace WildernessExtraction
{
    public static class CombatNoise
    {
        public static event Action<Vector3, float> Emitted;
        public static void Emit(Vector3 position, float radius)
        {
            if (radius > 0f) Emitted?.Invoke(position, radius);
        }
    }

    /// <summary>
    /// 射线武器：按住左键按射击间隔连发，R 换弹，空弹匣扣扳机自动换弹，翻滚中不可射击。
    /// 参考鸭科夫：1/2 或滚轮切主/副武器（切换会中断换弹，需等待装备时间），右键精确瞄准降低散布，
    /// V 不切武器直接近战一次（背包里有近战武器时用它，否则徒手），C 收起/取出武器；奔跑、收枪、用物品时不能开火。
    /// </summary>
    public sealed class PlayerShooter : MonoBehaviour
    {
        [SerializeField] private WeaponConfig config;
        [SerializeField] private WeaponConfig secondaryWeapon;
        [SerializeField] private ItemDefinition primaryWeaponItem;
        [SerializeField] private ItemDefinition secondaryWeaponItem;
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private Transform muzzle;
        [SerializeField] private LineRenderer tracer;
        [SerializeField, Min(0f)] private float tracerDuration = 0.05f;
        [SerializeField] private LayerMask hitMask = ~0;

        private const float MeleeHitCameraKick = 0.28f;

        [Header("V 近战：没有近战武器时的徒手数值（占位）")]
        [SerializeField, Min(0f)] private float unarmedDamage = 8f;
        [SerializeField, Min(0.1f)] private float unarmedRange = 1.3f;
        [SerializeField, Range(0f, 180f)] private float unarmedAngle = 90f;
        [SerializeField, Min(0f)] private float unarmedStaminaCost = 10f;
        [SerializeField, Min(0.05f)] private float unarmedInterval = 0.6f;
        [SerializeField, Min(0f)] private float unarmedSoundRadius = 2f;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[16];
        private readonly WeaponState[] weapons = new WeaponState[2];
        private readonly WeaponConfig[] configs = new WeaponConfig[2];
        private PlayerInventory inventory;
        private int activeSlot;
        private float tracerHideTime;
        private float equipReadyTime;
        private float nextQuickMeleeTime;
        private int meleeSweepSign = 1;
        private PlayerItemUser itemUser;

        public event Action Fired;
        public event Action ReloadStarted;
        /// <summary>想换弹（按 R 或空弹匣扣扳机）但背包里没有这种子弹。</summary>
        public event Action OutOfAmmo;

        public WeaponState Weapon => weapons[activeSlot];
        public WeaponConfig CurrentConfig => configs[activeSlot];
        public int ActiveSlot => activeSlot;
        public event Action<int> WeaponChanged;
        /// <summary>按下的武器槽没有可用武器（未配置或背包里没有）。</summary>
        public event Action<int> SlotUnavailable;
        /// <summary>参数为是否收起。</summary>
        public event Action<bool> HolsterChanged;
        /// <summary>V 键近战挥出（不论是否命中）。</summary>
        public event Action QuickMeleePerformed;
        /// <summary>任何一次近战挥砍（左键短刀或 V 键）；参数为挥动方向，+1 从左到右，-1 从右到左。</summary>
        public event Action<int> MeleeSwung;

        /// <summary>练习弹（靶场）：换弹不需要也不消耗背包里的子弹，但弹匣照常会打空、需要换弹。</summary>
        public bool FreeAmmo { get; set; }

        /// <summary>空手：不能开火，移速提高。左键、C、1/2 都会取出武器。</summary>
        public bool IsHolstered { get; private set; }
        /// <summary>刚切换武器、还不能攻击。</summary>
        public bool IsEquipping => Time.time < equipReadyTime;

        public ItemDefinition SlotItem(int slot) => slot == 0 ? primaryWeaponItem : slot == 1 ? secondaryWeaponItem : null;

        private PlayerItemUser ItemUser => itemUser != null ? itemUser : itemUser = GetComponent<PlayerItemUser>();
        private bool UsingItem => ItemUser != null && ItemUser.IsUsing;

        public bool TryEquip(ItemDefinition item)
        {
            var requestedSlot = item == primaryWeaponItem ? 0 : item == secondaryWeaponItem ? 1 : -1;
            return requestedSlot >= 0 && TrySelectSlot(requestedSlot);
        }

        /// <summary>切到指定武器槽并取出武器。槽位没有可用武器时返回 false 并发出 SlotUnavailable。</summary>
        public bool TrySelectSlot(int slot)
        {
            if (!IsSlotAvailable(slot))
            {
                SlotUnavailable?.Invoke(slot);
                return false;
            }

            if (slot != activeSlot)
            {
                Weapon.CancelReload();
                activeSlot = slot;
                equipReadyTime = Time.time + CurrentConfig.equipDuration;
                SetHolstered(false, false);
                WeaponChanged?.Invoke(activeSlot);
            }
            else
            {
                SetHolstered(false);
            }

            return true;
        }

        public void SetHolstered(bool holstered) => SetHolstered(holstered, true);

        private void SetHolstered(bool holstered, bool applyEquipDelay)
        {
            if (IsHolstered == holstered) return;
            IsHolstered = holstered;
            if (holstered)
            {
                Weapon.CancelReload();
            }
            else if (applyEquipDelay)
            {
                equipReadyTime = Time.time + CurrentConfig.equipDuration;
            }

            HolsterChanged?.Invoke(holstered);
        }

        private bool IsSlotAvailable(int slot)
        {
            if (slot < 0 || slot >= configs.Length || configs[slot] == null) return false;
            var item = SlotItem(slot);
            return item == null || inventory.Inventory.CountOf(item) > 0;
        }

        private void CycleWeapon()
        {
            var other = 1 - activeSlot;
            if (IsSlotAvailable(other)) TrySelectSlot(other);
        }

        private void Awake()
        {
            if (input == null)
            {
                input = GetComponent<PlayerInputReader>();
            }

            if (motor == null)
            {
                motor = GetComponent<PlayerMotor>();
            }

            inventory = GetComponent<PlayerInventory>();
            configs[0] = config;
            configs[1] = secondaryWeapon;
            for (var i = 0; i < configs.Length; i++)
            {
                var definition = configs[i];
                if (definition == null) continue;
                // 近战武器不用弹匣：给一个用不完的弹匣，挥砍时不会打空、也不会触发换弹（和换弹动画）。
                var magazine = definition.kind == WeaponKind.Melee ? int.MaxValue : Mathf.Max(1, definition.magazineSize);
                weapons[i] = new WeaponState(magazine, definition.fireInterval, definition.reloadDuration);
            }
            if (tracer != null)
            {
                tracer.enabled = false;
            }
        }

        private void Start()
        {
            // 快捷栏前两格显示主/副武器（鸭科夫式 1/2 武器、3–8 物品）。
            var quickSlots = inventory.QuickSlots;
            for (var slot = 0; slot < 2; slot++)
            {
                if (SlotItem(slot) != null) quickSlots.Set(slot, SlotItem(slot));
            }
        }

        private void OnDisable()
        {
            if (tracer != null)
            {
                tracer.enabled = false;
            }
        }

        private void Update()
        {
            var now = Time.time;
            var weapon = Weapon;
            var beforeTick = weapon.AmmoInMagazine;
            weapon.Tick(now);
            if (beforeTick != weapon.AmmoInMagazine && CurrentConfig.ammoItem != null && !FreeAmmo)
            {
                inventory.Inventory.Remove(CurrentConfig.ammoItem, weapon.AmmoInMagazine - beforeTick);
            }

            var slot = input.WeaponSlotPressed;
            if (slot >= 0)
            {
                TrySelectSlot(slot);
            }
            else if (input.CycleWeaponDirection != 0 && !IsEquipping)
            {
                CycleWeapon();
            }

            if (input.HolsterPressed)
            {
                SetHolstered(!IsHolstered);
            }

            if (input.MeleePressed)
            {
                TryQuickMelee(now);
            }

            weapon = Weapon;
            if (input.ReloadPressed && !IsHolstered && !UsingItem)
            {
                TryReload(now, true);
            }

            if (input.AttackHeld && IsHolstered)
            {
                if (input.AttackPressedThisFrame) SetHolstered(false);
            }
            else if (input.AttackHeld && !motor.IsDodging && !motor.IsSprinting && !UsingItem && now >= equipReadyTime)
            {
                if (weapon.AmmoInMagazine == 0)
                {
                    // 按住扣扳机时每帧都会尝试，只在按下那一帧提示没子弹。
                    TryReload(now, input.AttackPressedThisFrame);
                }
                else if (weapon.TryFire(now))
                {
                    Fire();
                }
            }

            if (tracer != null && tracer.enabled && now >= tracerHideTime)
            {
                tracer.enabled = false;
            }
        }

        private void TryReload(float now, bool notifyOutOfAmmo)
        {
            if (CurrentConfig.kind == WeaponKind.Melee) return;
            var available = CurrentConfig.ammoItem == null || FreeAmmo ? Weapon.MagazineSize : inventory.Inventory.CountOf(CurrentConfig.ammoItem);
            if (Weapon.TryStartReload(now, available))
            {
                ReloadStarted?.Invoke();
            }
            else if (notifyOutOfAmmo && available <= 0 && !Weapon.IsReloading && Weapon.AmmoInMagazine < Weapon.MagazineSize)
            {
                OutOfAmmo?.Invoke();
            }
        }

        private void Fire()
        {
            if (CurrentConfig.kind == WeaponKind.Melee)
            {
                FireMelee();
                return;
            }

            var origin = muzzle != null ? muzzle.position : transform.position + Vector3.up;
            var direction = motor.AimDirection;
            var rays = Mathf.Max(1, CurrentConfig.rayCount);
            var spread = input.Move.sqrMagnitude > 0.01f ? CurrentConfig.movingSpread : CurrentConfig.standingSpread;
            if (motor.IsAiming) spread *= CurrentConfig.adsSpreadMultiplier;
            var end = origin + direction * CurrentConfig.range;
            for (var i = 0; i < rays; i++)
            {
                var shotDirection = Quaternion.AngleAxis(UnityEngine.Random.Range(-spread, spread), Vector3.up) * direction;
                if (TryFindHit(origin, shotDirection, out var hit))
                {
                    end = hit.point;
                    var target = hit.collider.GetComponentInParent<Health>();
                    if (target != null)
                    {
                        var falloff = hit.distance <= CurrentConfig.effectiveRange ? 1f : Mathf.Lerp(1f, CurrentConfig.minimumDamageMultiplier,
                            Mathf.InverseLerp(CurrentConfig.effectiveRange, CurrentConfig.range, hit.distance));
                        target.TakeDamage(CurrentConfig.damage * falloff, gameObject);
                    }
                }
            }

            ShowTracer(origin, end);
            CombatNoise.Emit(origin, CurrentConfig.soundRadius);
            Fired?.Invoke();
        }

        private void FireMelee()
        {
            if (!motor.Stamina.TryConsume(CurrentConfig.staminaCost)) return;
            ApplyMelee(CurrentConfig.damage, CurrentConfig.range, CurrentConfig.meleeAngle, CurrentConfig.soundRadius);
            Fired?.Invoke();
        }

        /// <summary>V 键：不切换当前武器直接挥一次。优先用武器槽里的近战武器，没有则徒手。</summary>
        public bool TryQuickMelee(float now)
        {
            if (motor.IsDodging || UsingItem || now < nextQuickMeleeTime) return false;
            var melee = FindMeleeWeapon();
            var cost = melee != null ? melee.staminaCost : unarmedStaminaCost;
            if (!motor.Stamina.TryConsume(cost)) return false;
            nextQuickMeleeTime = now + (melee != null ? melee.fireInterval : unarmedInterval);
            if (melee != null) ApplyMelee(melee.damage, melee.range, melee.meleeAngle, melee.soundRadius);
            else ApplyMelee(unarmedDamage, unarmedRange, unarmedAngle, unarmedSoundRadius);
            QuickMeleePerformed?.Invoke();
            Fired?.Invoke();
            return true;
        }

        private WeaponConfig FindMeleeWeapon()
        {
            for (var slot = 0; slot < configs.Length; slot++)
            {
                if (configs[slot] != null && configs[slot].kind == WeaponKind.Melee && IsSlotAvailable(slot)) return configs[slot];
            }

            return null;
        }

        private void ApplyMelee(float damage, float range, float angle, float soundRadius)
        {
            var origin = transform.position;
            var hits = Physics.OverlapSphere(origin, range, hitMask, QueryTriggerInteraction.Ignore);
            var damaged = new System.Collections.Generic.HashSet<Health>();
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                var direction = hit.transform.position - origin; direction.y = 0f;
                if (Vector3.Angle(motor.AimDirection, direction) > angle * 0.5f) continue;
                var target = hit.GetComponentInParent<Health>();
                if (target != null && damaged.Add(target)) target.TakeDamage(damage, gameObject);
            }
            if (damaged.Count > 0) CombatNoise.Emit(origin, soundRadius);
            ShowMelee(origin, range, angle, damaged);
        }

        private void ShowMelee(Vector3 origin, float range, float angle, System.Collections.Generic.HashSet<Health> damaged)
        {
            // 连续挥砍左右交替，每刀都看得出是新的一刀。
            meleeSweepSign = -meleeSweepSign;
            MeleeSlashEffect.Play(transform, motor.AimDirection, range, angle, meleeSweepSign, MeleeSlashEffect.PlayerColor);
            MeleeSwung?.Invoke(meleeSweepSign);
            if (damaged.Count == 0) return;
            foreach (var target in damaged) MeleeHitEffect.Play(target, origin);
            var view = Camera.main;
            if (view != null && view.TryGetComponent<FixedObliqueCamera>(out var viewCamera)) viewCamera.Kick(motor.AimDirection * MeleeHitCameraKick);
        }

        private bool TryFindHit(Vector3 origin, Vector3 direction, out RaycastHit closest)
        {
            closest = default;
            var count = Physics.RaycastNonAlloc(origin, direction, hitBuffer, CurrentConfig.range, hitMask, QueryTriggerInteraction.Ignore);
            var found = false;
            for (var i = 0; i < count; i++)
            {
                var hit = hitBuffer[i];
                if (hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (!found || hit.distance < closest.distance)
                {
                    closest = hit;
                    found = true;
                }
            }

            return found;
        }

        private void ShowTracer(Vector3 from, Vector3 to)
        {
            if (tracer == null)
            {
                return;
            }

            tracer.positionCount = 2;
            tracer.SetPosition(0, from);
            tracer.SetPosition(1, to);
            tracer.enabled = true;
            tracerHideTime = Time.time + tracerDuration;
        }
    }
}
