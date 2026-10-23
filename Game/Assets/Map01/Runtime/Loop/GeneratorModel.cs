using System;
using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>指南 8.3：Unknown → Discovered → NeedsRepair → Operational → Producing，可被破坏进入 Damaged。</summary>
    public enum GeneratorState
    {
        Unknown,
        Discovered,
        NeedsRepair,
        Operational,
        Producing,
        Damaged
    }

    /// <summary>发电机纯规则：发现、限时修复、持续产电与取电。由权威层驱动，不依赖场景。</summary>
    public sealed class GeneratorModel
    {
        private readonly float repairDuration;
        private readonly float secondsPerCharge;
        private readonly int maxCharge;
        private float repairElapsed;
        private float chargeProgress;

        public GeneratorModel(float repairDuration, float secondsPerCharge, int maxCharge)
        {
            if (repairDuration <= 0f || secondsPerCharge <= 0f || maxCharge <= 0) throw new ArgumentOutOfRangeException();
            this.repairDuration = repairDuration;
            this.secondsPerCharge = secondsPerCharge;
            this.maxCharge = maxCharge;
        }

        public GeneratorState State { get; private set; } = GeneratorState.Unknown;
        public bool IsRepairing { get; private set; }
        public float RepairProgress => Mathf.Clamp01(repairElapsed / repairDuration);
        public int StoredCharge { get; private set; }
        public int MaxCharge => maxCharge;
        /// <summary>下一格电量的进度 0–1。</summary>
        public float ChargeProgress => StoredCharge >= maxCharge ? 1f : chargeProgress / secondsPerCharge;

        /// <summary>玩家第一次靠近：标记已发现并进入待修复。</summary>
        public bool Discover()
        {
            if (State != GeneratorState.Unknown) return false;
            State = GeneratorState.NeedsRepair;
            return true;
        }

        public bool BeginRepair()
        {
            if (State == GeneratorState.Unknown) Discover();
            if (State != GeneratorState.NeedsRepair && State != GeneratorState.Damaged || IsRepairing) return false;
            IsRepairing = true;
            repairElapsed = 0f;
            return true;
        }

        public void CancelRepair()
        {
            IsRepairing = false;
            repairElapsed = 0f;
        }

        /// <summary>推进修复与产电。修复中离开范围会取消。返回本帧是否刚修好。</summary>
        public bool Tick(float deltaTime, bool repairerInRange)
        {
            var repaired = false;
            if (IsRepairing)
            {
                if (!repairerInRange)
                {
                    CancelRepair();
                }
                else
                {
                    repairElapsed += deltaTime;
                    if (repairElapsed >= repairDuration)
                    {
                        IsRepairing = false;
                        State = GeneratorState.Producing;
                        repaired = true;
                    }
                }
            }

            if (State == GeneratorState.Producing || State == GeneratorState.Operational)
            {
                if (StoredCharge < maxCharge)
                {
                    chargeProgress += deltaTime;
                    while (chargeProgress >= secondsPerCharge && StoredCharge < maxCharge)
                    {
                        chargeProgress -= secondsPerCharge;
                        StoredCharge++;
                    }
                }

                // 存满后保持运转但不再产电；取走电量后回到产电状态。
                var full = StoredCharge >= maxCharge;
                State = full ? GeneratorState.Operational : GeneratorState.Producing;
                if (full) chargeProgress = 0f;
            }

            return repaired;
        }

        /// <summary>取一格电给空电池。</summary>
        public bool TryTakeCharge()
        {
            if (StoredCharge <= 0) return false;
            StoredCharge--;
            return true;
        }

        public void Damage()
        {
            if (State == GeneratorState.Producing || State == GeneratorState.Operational)
            {
                State = GeneratorState.Damaged;
                IsRepairing = false;
            }
        }
    }
}
