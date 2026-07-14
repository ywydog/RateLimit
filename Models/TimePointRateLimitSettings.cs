using ClassIsland.RateLimit.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.RateLimit.Models;

/// <summary>
/// 时间点模式：到达 <see cref="TimePoints"/> 中任一时间点（HH:mm）时，窗口内最多 <see cref="RateLimitBaseSettings.MaxCount"/> 次。
/// </summary>
public partial class TimePointRateLimitSettings : RateLimitBaseSettings
{
    /// <summary>
    /// 时间点列表，多个用英文逗号分隔，例如 "08:00,12:00,18:00"。
    /// </summary>
    [ObservableProperty]
    private string _timePoints = "08:00,12:00,18:00";

    internal override bool IsInMode(DateTime now, Guid workflowId, RateLimitService service)
    {
        var nowMinutes = now.Hour * 60 + now.Minute;
        var points = ParseTimePoints(TimePoints);
        if (points.Count == 0) return true;

        // 当前时间是否在某个时间点 ± 1 分钟内
        return points.Any(p => Math.Abs(p - nowMinutes) <= 1);
    }

    internal override string ModeName => "时间点";

    private static List<int> ParseTimePoints(string text)
    {
        var result = new List<int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseTimeOfDay(part, out var minutes)) result.Add(minutes);
        }
        return result;
    }

    private static bool TryParseTimeOfDay(string text, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        if (h is < 0 or > 23 || m is < 0 or > 59) return false;
        minutes = h * 60 + m;
        return true;
    }
}
