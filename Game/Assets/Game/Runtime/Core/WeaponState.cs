using System;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>弹匣、射击间隔和换弹计时。时间由调用方传入，便于测试。</summary>
    public sealed class WeaponState
    {
        private readonly float fireInterval;
        private readonly float reloadDuration;
        private float nextFireTime = float.NegativeInfinity;
        private float reloadStartTime;
        private float reloadEndTime;
        private int pendingReloadAmount;

        public WeaponState(int magazineSize, float fireInterval, float reloadDuration)
        {
            if (magazineSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(magazineSize));
            }

            MagazineSize = magazineSize;
            AmmoInMagazine = magazineSize;
            this.fireInterval = Mathf.Max(0f, fireInterval);
            this.reloadDuration = Mathf.Max(0f, reloadDuration);
        }

        public int MagazineSize { get; }
        public int AmmoInMagazine { get; private set; }
        public bool IsReloading { get; private set; }

        public float ReloadProgress(float now)
        {
            if (!IsReloading)
            {
                return 0f;
            }

            return reloadDuration <= 0f ? 1f : Mathf.Clamp01((now - reloadStartTime) / reloadDuration);
        }

        public bool CanFire(float now) => !IsReloading && AmmoInMagazine > 0 && now >= nextFireTime;

        public bool TryFire(float now)
        {
            Tick(now);
            if (!CanFire(now))
            {
                return false;
            }

            AmmoInMagazine--;
            nextFireTime = now + fireInterval;
            return true;
        }

        public bool TryStartReload(float now) => TryStartReload(now, MagazineSize - AmmoInMagazine);

        public bool TryStartReload(float now, int availableRounds)
        {
            Tick(now);
            if (IsReloading || AmmoInMagazine >= MagazineSize || availableRounds <= 0)
            {
                return false;
            }

            IsReloading = true;
            reloadStartTime = now;
            reloadEndTime = now + reloadDuration;
            pendingReloadAmount = Math.Min(availableRounds, MagazineSize - AmmoInMagazine);
            return true;
        }

        public void Tick(float now)
        {
            if (IsReloading && now >= reloadEndTime)
            {
                IsReloading = false;
                AmmoInMagazine += pendingReloadAmount;
                pendingReloadAmount = 0;
            }
        }
    }
}
