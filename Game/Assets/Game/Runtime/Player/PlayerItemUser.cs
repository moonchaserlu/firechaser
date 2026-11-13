using System;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 使用消耗品（参考鸭科夫）：数字键 3–8 使用快捷栏里的物品，背包内选中物品按 U 使用。
    /// 读条期间可以慢速移动、不能开火；翻滚、切换/收起武器或再按一次同一快捷键会取消，取消不消耗物品。
    /// 新捡到的可用物品自动放进 3–8 中第一个空槽。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerItemUser : MonoBehaviour
    {
        /// <summary>快捷栏下标 0、1 是主/副武器，从 2（数字键 3）开始是物品。</summary>
        public const int FirstItemSlot = 2;

        private readonly TimedProgress use = new TimedProgress();
        private PlayerInputReader input;
        private PlayerInventory inventory;
        private PlayerMotor motor;
        private PlayerShooter shooter;
        private Health health;
        private BleedingStatus bleeding;

        public event Action<ItemDefinition> UseStarted;
        public event Action<ItemDefinition> UseCompleted;
        public event Action<ItemDefinition> UseCancelled;
        /// <summary>无法使用，参数二为给玩家看的原因。</summary>
        public event Action<ItemDefinition, string> UseRejected;

        public bool IsUsing => UsingItem != null;
        public ItemDefinition UsingItem { get; private set; }
        public float Progress => use.Progress;

        private void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            inventory = GetComponent<PlayerInventory>();
            motor = GetComponent<PlayerMotor>();
            shooter = GetComponent<PlayerShooter>();
            health = GetComponent<Health>();
            bleeding = GetComponent<BleedingStatus>();
        }

        private void OnEnable()
        {
            if (motor != null) motor.DodgeStarted += Cancel;
            if (shooter != null)
            {
                shooter.WeaponChanged += OnWeaponChanged;
                shooter.HolsterChanged += OnHolsterChanged;
            }

            if (health != null) health.Died += Cancel;
            if (inventory != null) inventory.Inventory.Changed += AutoAssignQuickSlots;
        }

        private void OnDisable()
        {
            if (motor != null) motor.DodgeStarted -= Cancel;
            if (shooter != null)
            {
                shooter.WeaponChanged -= OnWeaponChanged;
                shooter.HolsterChanged -= OnHolsterChanged;
            }

            if (health != null) health.Died -= Cancel;
            if (inventory != null) inventory.Inventory.Changed -= AutoAssignQuickSlots;
            Cancel();
        }

        private void Start()
        {
            AutoAssignQuickSlots();
        }

        private void Update()
        {
            if (IsUsing)
            {
                if (inventory.Inventory.CountOf(UsingItem) == 0)
                {
                    Cancel();
                }
                else if (use.Tick(Time.deltaTime))
                {
                    Complete();
                }
            }

            if (!input.GameplayEnabled) return;
            var slot = input.QuickSlotPressed;
            if (slot < FirstItemSlot) return;
            var item = inventory.QuickSlots[slot];
            if (item == null) return;
            if (IsUsing && item == UsingItem) Cancel();
            else TryUse(item);
        }

        /// <summary>开始使用物品；正在用其他物品时先取消它。</summary>
        public bool TryUse(ItemDefinition item)
        {
            if (!isActiveAndEnabled || (motor != null && motor.IsDodging)) return false;
            var check = ItemUseRules.Check(item, inventory.Inventory.CountOf(item), health.Current, health.Max, bleeding != null && bleeding.IsBleeding);
            if (check != ItemUseCheck.Ok)
            {
                UseRejected?.Invoke(item, ItemUseRules.Describe(check, item));
                return false;
            }

            Cancel();
            UsingItem = item;
            use.Start(item.useDuration);
            UseStarted?.Invoke(item);
            return true;
        }

        public void Cancel()
        {
            if (!IsUsing) return;
            var item = UsingItem;
            UsingItem = null;
            use.Cancel();
            UseCancelled?.Invoke(item);
        }

        private void Complete()
        {
            var item = UsingItem;
            UsingItem = null;
            if (inventory.Inventory.Remove(item, 1) == 0) return;
            if (item.healAmount > 0f) health.Heal(item.healAmount);
            if (item.stopsBleeding && bleeding != null) bleeding.StopBleeding();
            UseCompleted?.Invoke(item);
        }

        private void OnWeaponChanged(int slot) => Cancel();

        private void OnHolsterChanged(bool holstered) => Cancel();

        private void AutoAssignQuickSlots()
        {
            var quickSlots = inventory.QuickSlots;
            foreach (var stack in inventory.Inventory.Stacks)
            {
                if (stack.Item.IsUsable) quickSlots.AutoAssign(stack.Item, FirstItemSlot);
            }
        }
    }
}
