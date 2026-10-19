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

    /// <summary>射线武器：按住左键按射击间隔连发，R 换弹，空弹匣扣扳机自动换弹，翻滚中不可射击。</summary>
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

        private readonly RaycastHit[] hitBuffer = new RaycastHit[16];
        private readonly WeaponState[] weapons = new WeaponState[2];
        private readonly WeaponConfig[] configs = new WeaponConfig[2];
        private PlayerInventory inventory;
        private int activeSlot;
        private float tracerHideTime;

        public event Action Fired;
        public event Action ReloadStarted;

        public WeaponState Weapon => weapons[activeSlot];
        public WeaponConfig CurrentConfig => configs[activeSlot];
        public int ActiveSlot => activeSlot;
        public event Action<int> WeaponChanged;

        public bool TryEquip(ItemDefinition item)
        {
            var requestedSlot = item == primaryWeaponItem ? 0 : item == secondaryWeaponItem ? 1 : -1;
            if (requestedSlot < 0 || configs[requestedSlot] == null || inventory.Inventory.CountOf(item) == 0) return false;
            if (requestedSlot != activeSlot)
            {
                activeSlot = requestedSlot;
                WeaponChanged?.Invoke(activeSlot);
            }
            return true;
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
                if (definition != null) weapons[i] = new WeaponState(Mathf.Max(1, definition.magazineSize), definition.fireInterval, definition.reloadDuration);
            }
            if (tracer != null)
            {
                tracer.enabled = false;
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
            if (beforeTick != weapon.AmmoInMagazine && CurrentConfig.ammoItem != null)
            {
                inventory.Inventory.Remove(CurrentConfig.ammoItem, weapon.AmmoInMagazine - beforeTick);
            }

            if (input.ReloadPressed)
            {
                TryReload(now);
            }

            if (input.AttackHeld && !motor.IsDodging)
            {
                if (weapon.AmmoInMagazine == 0)
                {
                    TryReload(now);
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

        private void TryReload(float now)
        {
            var available = CurrentConfig.ammoItem == null ? Weapon.MagazineSize : inventory.Inventory.CountOf(CurrentConfig.ammoItem);
            if (Weapon.TryStartReload(now, available))
            {
                ReloadStarted?.Invoke();
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
                        target.TakeDamage(CurrentConfig.damage * falloff);
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
            var origin = transform.position;
            var hits = Physics.OverlapSphere(origin, CurrentConfig.range, hitMask, QueryTriggerInteraction.Ignore);
            var damaged = new System.Collections.Generic.HashSet<Health>();
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                var direction = hit.transform.position - origin; direction.y = 0f;
                if (Vector3.Angle(motor.AimDirection, direction) > CurrentConfig.meleeAngle * 0.5f) continue;
                var target = hit.GetComponentInParent<Health>();
                if (target != null && damaged.Add(target)) target.TakeDamage(CurrentConfig.damage);
            }
            if (damaged.Count > 0) CombatNoise.Emit(origin, CurrentConfig.soundRadius);
            Fired?.Invoke();
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
