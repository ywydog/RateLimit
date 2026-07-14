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
        _logger.LogInformation("RateLimitService 已实例化。");
    }

    public bool CanExecute(RateLimitBaseSettings settings, DateTime now)
    {
        var workflowId = ResolveWorkflowId(settings);
        if (workflowId == Guid.Empty)
        {
            _logger.LogWarning(
                "CanExecute：无法定位 settings 所属工作流（模式={Mode}），默认放行。",
                settings.ModeName);
            return true;
        }

        // 模式判断（多态）
        if (!settings.IsInMode(now, workflowId, this))
        {
            _logger.LogDebug(
                "限频拦截：当前时间不在模式允许窗口内。模式={Mode}，工作流={WorkflowId}，时间={Now:O}",
                settings.ModeName, workflowId, now);
            return false;
        }

        // 限频判断
        var history = LoadHistory(workflowId);
        PruneOldEntries(history, settings.WindowSeconds, now);

        if (history.Count >= settings.MaxCount)
        {
            _logger.LogInformation(
                "限频拦截：窗口内已执行 {Count}/{Max} 次。模式={Mode}，工作流={WorkflowId}，窗口={Window}s",
                history.Count, settings.MaxCount, settings.ModeName, workflowId, settings.WindowSeconds);
            return false;
        }

        // 放行 → 入列
        history.Add(now);
        SaveHistory(workflowId, history);
        _logger.LogDebug(
            "限频放行：执行 {Count}/{Max}（+1）。模式={Mode}，工作流={WorkflowId}，时间={Now:O}",
            history.Count, settings.MaxCount, settings.ModeName, workflowId, now);
        return true;
    }

    public void Record(Guid workflowId, DateTime now)
    {
        if (workflowId == Guid.Empty)
        {
            _logger.LogWarning("Record：收到 Guid.Empty 工作流，跳过。");
            return;
        }

        var history = LoadHistory(workflowId);
        // 兜底用 30 天裁剪，避免无主 history 无限增长
        PruneOldEntries(history, FallbackPruneWindowSeconds, now);
        history.Add(now);
        SaveHistory(workflowId, history);
        _logger.LogDebug(
            "Record：已记录一次执行（{Count} 条历史保留）。工作流={WorkflowId}，时间={Now:O}",
            history.Count, workflowId, now);
    }

    public void Reset(Guid workflowId)
    {
        if (workflowId == Guid.Empty)
        {
            _logger.LogWarning("Reset：收到 Guid.Empty 工作流，跳过。");
            return;
        }
        GlobalStorageService.SetValue(StorageKeyPrefix + workflowId, null);
        _logger.LogInformation("Reset：已清除工作流限频历史。工作流={WorkflowId}", workflowId);
    }

    public int GetCount(Guid workflowId)
    {
        var count = LoadHistory(workflowId).Count;
        _logger.LogDebug("GetCount：工作流={WorkflowId}，当前计数={Count}", workflowId, count);
        return count;
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
        if (settings.CachedWorkflowId is { } cached)
        {
            _logger.LogTrace(
                "ResolveWorkflowId 命中缓存：模式={Mode}，工作流={WorkflowId}",
                settings.ModeName, cached);
            return cached;
        }

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
                _logger.LogDebug(
                    "ResolveWorkflowId 扫描命中：模式={Mode}，工作流={WorkflowId}（共扫 {Scanned} 个工作流）",
                    settings.ModeName, wf.ActionSet.Guid, workflows.Length);
                return wf.ActionSet.Guid;
            }
        }

        // 未命中：不缓存，下次重试
        _logger.LogDebug(
            "ResolveWorkflowId 扫描未命中：模式={Mode}（共扫 {Scanned} 个工作流），下次重试。",
            settings.ModeName, workflows.Length);
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
                // 持久化 JSON 损坏时丢弃旧数据，避免持续抛异常阻塞限频判断。
                // 静默兜底外层不抛；需要排障时打开 [General]Debug 日志级别或自行查看 GlobalStorageService。
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
