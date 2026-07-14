using ClassIsland.RateLimit.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.RateLimit.Models;

/// <summary>
/// 时间间隔模式：每 <see cref="IntervalSeconds"/> 秒允许一次，窗口内最多 <see cref="RateLimitBaseSettings.MaxCount"/> 次。
/// </summary>
public partial class IntervalRateLimitSettings : RateLimitBaseSettings
{
    /// <summary>
    /// 触发间隔（秒）。距上次执行小于该值则拒绝。
    /// </summary>
    [ObservableProperty]
    private int _intervalSeconds = 60;

    internal override bool IsInMode(DateTime now, Guid workflowId, RateLimitService service)
    {
        if (IntervalSeconds <= 0) return true;
        var history = service.LoadHistoryInternal(workflowId);
        if (history.Count == 0) return true; // 没有历史 → 首次放行
        var last = history[^1];
        return (now - last).TotalSeconds >= IntervalSeconds;
    }

    internal override string ModeName => "时间间隔";
}
