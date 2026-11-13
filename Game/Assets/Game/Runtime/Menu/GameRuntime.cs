using System;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>跨场景常驻：启动时应用设置、切到后台静音、给当前存档槽累计游戏时长。</summary>
    public sealed class GameRuntime : MonoBehaviour
    {
        private const float FlushInterval = 30f;

        private double pendingSeconds;
        private float flushTimer;
        private bool focused = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<GameRuntime>() != null) return;
            var go = new GameObject("[GameRuntime]");
            DontDestroyOnLoad(go);
            go.AddComponent<GameRuntime>();
            GameSettings.Current.Apply();
        }

        private void Update()
        {
            // 暂停时 timeScale 为 0，不计时长。
            if (SaveSlots.ActiveSlot == 0) return;
            pendingSeconds += Time.deltaTime;
            flushTimer += Time.unscaledDeltaTime;
            if (flushTimer >= FlushInterval) Flush();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            var settings = GameSettings.Current;
            AudioListener.volume = !focused && settings.muteInBackground ? 0f : settings.masterVolume / 100f;
        }

        private void OnApplicationQuit() => Flush();

        /// <summary>把累计的时长写进当前槽位。切场景、回主菜单前也会调用。</summary>
        public void Flush()
        {
            flushTimer = 0f;
            if (pendingSeconds <= 0) return;
            SaveSlots.AddPlayTime(pendingSeconds, DateTime.UtcNow);
            pendingSeconds = 0;
        }

        public static void FlushActive()
        {
            var runtime = FindAnyObjectByType<GameRuntime>();
            if (runtime != null) runtime.Flush();
        }
    }
}
