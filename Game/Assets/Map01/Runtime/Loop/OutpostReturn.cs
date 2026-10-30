using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 前哨登陆点：从地下安全屋出发的一局里，玩家离开前哨足够远后，回到这里连续停留满时长，
    /// 就带着背包下到地下安全屋。直接打开 Map01（没有从安全屋出发）时不生效。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OutpostReturn : MonoBehaviour
    {
        [SerializeField] private Map01Authority authority;
        [SerializeField, Min(0.5f)] private float radius = 4f;
        [SerializeField, Min(0.1f)] private float duration = 3f;
        [Tooltip("离开前哨超过该距离后才启用返程，避免刚出发就回去。")]
        [SerializeField, Min(0f)] private float armDistance = 25f;

        private ExtractionTimer timer;
        private ReturnGate gate;
        private GUIStyle style;

        public float Radius => radius;
        public float Duration => duration;
        public bool IsArmed => gate != null && gate.IsArmed;
        public float Progress => timer != null ? timer.Progress : 0f;

        private Map01Authority Authority => authority != null ? authority : authority = FindAnyObjectByType<Map01Authority>();

        private void Awake()
        {
            timer = new ExtractionTimer(duration);
            gate = new ReturnGate(armDistance);
        }

        private void Update()
        {
            var auth = Authority;
            if (auth == null || !auth.CanReturnToShelter)
            {
                return;
            }

            var distance = Horizontal(auth.Player.transform.position);
            gate.Update(distance);
            var inside = gate.IsArmed && !auth.Player.Health.IsDead && distance <= radius;
            if (timer.Tick(inside, Time.deltaTime) == ExtractionTick.Completed)
            {
                auth.ReturnToShelter();
            }
        }

        private float Horizontal(Vector3 position)
        {
            var delta = position - transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        private void OnGUI()
        {
            var auth = Authority;
            if (auth == null || !auth.CanReturnToShelter || !gate.IsArmed || timer.Elapsed <= 0f) return;
            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            GUI.Box(new Rect(Screen.width * 0.5f - 200, Screen.height - 220, 400, 40), $"返回地下庇护所 {timer.Progress:P0}", style);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.7f, 0.3f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
