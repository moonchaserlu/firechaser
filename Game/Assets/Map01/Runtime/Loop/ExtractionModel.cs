using System;

namespace WildernessExtraction.Map01
{
    /// <summary>指南 8.4：Disabled → Candidate → Active → Boarding → Interrupted → Completed。</summary>
    public enum ExtractionState
    {
        Disabled,
        Candidate,
        Active,
        Boarding,
        Interrupted,
        Completed
    }

    public enum ExtractionInterruptReason
    {
        None,
        MonsterNearby,
        LeftZone,
        PowerLost
    }

    /// <summary>
    /// 救援撤离纯规则。开局两个候选点都不可用；安全屋电量达到要求后，本局选中的候选点激活。
    /// 玩家在区域内连续登机满时长即完成，有怪物靠近或离开区域则中断并给出原因。
    /// </summary>
    public sealed class ExtractionModel
    {
        private readonly float boardingDuration;

        public ExtractionModel(string chosenPointId, float boardingDuration)
        {
            if (string.IsNullOrEmpty(chosenPointId)) throw new ArgumentException("必须有一个本局撤离点。", nameof(chosenPointId));
            if (boardingDuration <= 0f) throw new ArgumentOutOfRangeException(nameof(boardingDuration));
            ChosenPointId = chosenPointId;
            this.boardingDuration = boardingDuration;
        }

        public string ChosenPointId { get; }
        public ExtractionState State { get; private set; } = ExtractionState.Candidate;
        public ExtractionInterruptReason LastInterrupt { get; private set; }
        public float BoardingElapsed { get; private set; }
        public float BoardingDuration => boardingDuration;
        public bool IsUsable => State == ExtractionState.Active || State == ExtractionState.Boarding || State == ExtractionState.Interrupted;

        /// <summary>救援条件满足：激活本局撤离点。</summary>
        public bool Activate()
        {
            if (State != ExtractionState.Candidate) return false;
            State = ExtractionState.Active;
            return true;
        }

        public ExtractionState Tick(float deltaTime, bool playerInside, bool monsterNearby)
        {
            if (!IsUsable) return State;

            if (State == ExtractionState.Boarding)
            {
                if (!playerInside) return Interrupt(ExtractionInterruptReason.LeftZone);
                if (monsterNearby) return Interrupt(ExtractionInterruptReason.MonsterNearby);
                BoardingElapsed += deltaTime;
                if (BoardingElapsed >= boardingDuration) State = ExtractionState.Completed;
                return State;
            }

            if (playerInside && !monsterNearby)
            {
                State = ExtractionState.Boarding;
                BoardingElapsed = 0f;
                LastInterrupt = ExtractionInterruptReason.None;
            }

            return State;
        }

        private ExtractionState Interrupt(ExtractionInterruptReason reason)
        {
            State = ExtractionState.Interrupted;
            LastInterrupt = reason;
            BoardingElapsed = 0f;
            return State;
        }
    }
}
