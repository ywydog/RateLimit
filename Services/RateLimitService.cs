using System.Text.Json;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Automation;
using ClassIsland.Core.Services;
using ClassIsland.RateLimit.Models;
using Microsoft.Extensions.Logging;

namespace ClassIsland.RateLimit.Services;

/// <summary>
/// 限频服务实现。
/// </summary>
/// <remarks>
/// 设计要点：
/// 1. 通过遍历 <see cref="IAutomationService.Workflows"/> 反向查找包含本 settings 实例的工作流。
/// 2. 用 <see cref="RateLimitBaseSettings.CachedWorkflowId"/> 缓存结果，惰性一次建立。
/// 3. 历史队列以 JSON 数组形式存到 <see cref="GlobalStorageService"/>，按工作流 GUID 分键。
/// 4. <see cref="CanExecute"/> 在放行时自动入列，<see cref="Record"/> 仅入列不动判断。
/// 5. 模式判断下沉到 <see cref="RateLimitBaseSettings.IsInMode"/> 多态分发。
/// 6. 持久化层加 <see cref="_storageLock"/> 防并发读写损坏 JSON。
/// 7. <see cref="Record"/> 兜底用 30 天裁剪，避免 history 无限增长。
/// </remarks>
public class RateLimitService : IRateLimitService
{
    private readonly ILogger<RateLimitService> _logger;
    private readonly IAutomationService _automationService;

    private const string StorageKeyPrefix = "classisland.ratelimit.history.";
    // 30 天兜底裁剪上限（仅 Record 单独调用、找不到对应 rule 时使用）
    private const int FallbackPruneWindowSeconds = 30 * 24 * 3600;

    // 持久化层读写锁，防止并发读写破坏 JSON
    private static readonly object _storageLock = new();

    public RateLimitService(ILogger<RateLimitService> logger, IAutomationService automationService)
    {
        _logger = logger;
        _automationService = automationService;
    }

    public bool CanExecute(RateLimitBaseSettings settings, DateTime now)
    {
        var workflowId = ResolveWorkflowId(settings);
        if (workflowId == Guid.Empty) return true; // 找不到所属工作流时默认放行

        // 模式判断（多态）
        if (!settings.IsInMode(now, workflowId, this)) return false;

        // 限频判断
        var history = LoadHistory(workflowId);
        PruneOldEntries(history, settings.WindowSeconds, now);

        if (history.Count >= settings.MaxCount) return false;

        // 放行 → 入列
        history.Add(now);
        SaveHistory(workflowId, history);
        return true;
    }

    public void Record(Guid workflowId, DateTime now)
    {
        if (workflowId == Guid.Empty) return;

        var history = LoadHistory(workflowId);
        // 兜底用 30 天裁剪，避免无主 history 无限增长
        PruneOldEntries(history, FallbackPruneWindowSeconds, now);
        history.Add(now);
        SaveHistory(workflowId, history);
    }

    public void Reset(Guid workflowId)
    {
        GlobalStorageService.SetValue(StorageKeyPrefix + workflowId, null);
    }

    public int GetCount(Guid workflowId)
    {
        return LoadHistory(workflowId).Count;
    }

    // ---------- 内部辅助（供派生 settings 调用） ----------

    /// <summary>
    /// 加载指定工作流的历史。供派生 settings 中的模式判断使用。
    /// </summary>
    internal List<DateTime> LoadHistoryInternal(Guid workflowId) => LoadHistory(workflowId);

    // ---------- 私有方法 ----------

    /// <summary>
    /// 反向查找 settings 所属工作流的 GUID。命中后写入 settings.CachedWorkflowId 缓存。
    /// </summary>
    /// <remarks>
    /// 关键修正：未命中时**不缓存**，允许下次重试。否则一旦首次调用时集合尚未初始化，
    /// 会永远返回 <see cref="Guid.Empty"/>。
    /// </remarks>
    private Guid ResolveWorkflowId(RateLimitBaseSettings settings)
    {
        if (settings.CachedWorkflowId is { } cached) return cached;

        // 防御性快照：避免枚举过程中集合被修改
        Workflow[] workflows;
        try
        {
            workflows = _automationService.Workflows.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "枚举 Workflows 失败，跳过本次反查。");
            return Guid.Empty;
        }

        foreach (var wf in workflows)
        {
            if (ReferenceEquals(wf.Ruleset, null)) continue;
            if (ContainsSettings(wf, settings))
            {
                settings.CachedWorkflowId = wf.ActionSet.Guid;
                return wf.ActionSet.Guid;
            }
        }

        // 未命中：不缓存，下次重试
        return Guid.Empty;
    }

    private static bool ContainsSettings(Workflow wf, RateLimitBaseSettings target)
    {
        foreach (var group in wf.Ruleset.Groups)
        foreach (var rule in group.Rules)
        {
            if (rule.Settings is RateLimitBaseSettings rs && ReferenceEquals(rs, target)) return true;
        }
        return false;
    }

    // ---------- 持久化 ----------

    private static List<DateTime> LoadHistory(Guid workflowId)
    {
        lock (_storageLock)
        {
            var raw = GlobalStorageService.GetValue(StorageKeyPrefix + workflowId);
            if (string.IsNullOrWhiteSpace(raw)) return new List<DateTime>();
            try
            {
                return JsonSerializer.Deserialize<List<DateTime>>(raw) ?? new List<DateTime>();
            }
            catch
            {
                return new List<DateTime>();
            }
        }
    }

    private static void SaveHistory(Guid workflowId, List<DateTime> history)
    {
        lock (_storageLock)
        {
            GlobalStorageService.SetValue(
                StorageKeyPrefix + workflowId,
                JsonSerializer.Serialize(history));
        }
    }

    private static void PruneOldEntries(List<DateTime> history, int windowSeconds, DateTime now)
    {
        if (windowSeconds <= 0) return;
        var cutoff = now - TimeSpan.FromSeconds(windowSeconds);
        history.RemoveAll(t => t < cutoff);
    }
}
