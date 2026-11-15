using System;

namespace WildernessExtraction
{
    /// <summary>能被 Esc 暂停的一局（Map01RunFlow、RunSession）。暂停菜单只认这个接口。</summary>
    public interface IPauseHost
    {
        bool IsPaused { get; }
        event Action<bool> PauseChanged;
        void SetPaused(bool paused);
    }
}
