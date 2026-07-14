using System.Text.Json.Serialization;
using ClassIsland.RateLimit.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.RateLimit.Models;

/// <summary>
/// 时间段模式：当前时间在 <c>[<see cref="TimeRangeStart"/>, <see cref="TimeRangeEnd"/>]</c> 区间内时，窗口内最多 <see cref="RateLimitBaseSettings.MaxCount"/> 次。
/// 支持跨天（如 22:00-06:00）。
/// </summary>
public partial class TimeRangeRateLimitSettings : RateLimitBaseSettings
{
    /// <summary>
    /// 开始时间。
    /// </summary>
    [ObservableProperty]
    private TimeSpan? _timeRangeStart = new TimeSpan(8, 0, 0);

    /// <summary>
    /// 结束时间。
    /// </summary>
    [ObservableProperty]
    private TimeSpan? _timeRangeEnd = new TimeSpan(12, 0, 0);

    /// <summary>
    /// UI 显示用：把 <see cref="TimeRangeStart"/> 格式化为 "HH:mm"。
    /// </summary>
    [JsonIgnore]
    public string TimeRangeStartText => TimeRangeStart is { } s ? $"{s.Hours:D2}:{s.Minutes:D2}" : "";

    /// <summary>
    /// UI 显示用：把 <see cref="TimeRangeEnd"/> 格式化为 "HH:mm"。
    /// </summary>
    [JsonIgnore]
    public string TimeRangeEndText => TimeRangeEnd is { } e ? $"{e.Hours:D2}:{e.Minutes:D2}" : "";

    partial void OnTimeRangeStartChanged(TimeSpan? value)
        => OnPropertyChanged(nameof(TimeRangeStartText));

    partial void OnTimeRangeEndChanged(TimeSpan? value)
        => OnPropertyChanged(nameof(TimeRangeEndText));

    internal override bool IsInMode(DateTime now, Guid workflowId, RateLimitService service)
    {
        if (TimeRangeStart is null || TimeRangeEnd is null) return true;
        var startMin = (int)TimeRangeStart.Value.TotalMinutes;
        var endMin = (int)TimeRangeEnd.Value.TotalMinutes;
        var nowMin = now.Hour * 60 + now.Minute;
        return startMin <= endMin
            ? nowMin >= startMin && nowMin <= endMin
            : nowMin >= startMin || nowMin <= endMin; // 跨天
    }
}
