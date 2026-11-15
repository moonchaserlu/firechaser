using UnityEngine;

namespace WildernessExtraction.Tutorial
{
    /// <summary>
    /// 事件选择「昏暗 / 长明」的 NPC：灯下的受伤寻火者。
    /// 玩家走近时转头看过来、说一句话；站在 <see cref="TalkRadius"/> 内按交互打开选择。
    /// 选项内容在 <see cref="TutorialConfig.choice"/>，后果由导演执行（改视野、写存档标记）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialChoicePoint : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float talkRadius = 2.4f;
        [Tooltip("玩家进入这个距离时 NPC 转头看过来，并说一句招呼。")]
        [SerializeField, Min(1f)] private float noticeRadius = 7f;
        [Tooltip("转头的那部分（模型根）；为空时转整个物体。")]
        [SerializeField] private Transform look;
        [SerializeField, TextArea(1, 2)] private string greeting = "……有人吗？过来，借着灯光让我看看你。";

        private Quaternion restRotation;
        private bool greeted;

        public float TalkRadius => talkRadius;
        public float NoticeRadius => noticeRadius;
        public string Greeting => greeting;
        /// <summary>选择做完后不再转头、不再招呼。</summary>
        public bool Done { get; set; }

        public void Configure(float talk, Transform lookAt)
        {
            talkRadius = talk;
            look = lookAt;
        }

        private void Awake()
        {
            if (look == null) look = transform;
            restRotation = look.rotation;
        }

        /// <summary>每帧由导演调用：玩家靠近时转头看向他，第一次靠近时返回 true（导演负责播报招呼）。</summary>
        public bool Watch(Vector3 player)
        {
            var delta = player - look.position;
            delta.y = 0f;
            var near = delta.magnitude <= noticeRadius && !Done;
            var target = near && delta.sqrMagnitude > 0.01f ? Quaternion.LookRotation(delta.normalized, Vector3.up) : restRotation;
            look.rotation = Quaternion.Slerp(look.rotation, target, 1f - Mathf.Exp(-4f * Time.deltaTime));
            if (!near || greeted) return false;
            greeted = true;
            return true;
        }
    }
}
