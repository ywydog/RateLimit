using ClassIsland.Core.Abstractions.Services;
using ClassIsland.RateLimit.Models;
using ClassIsland.RateLimit.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClassIsland.RateLimit;

/// <summary>
/// 在应用启动时把三个限频规则的处理函数挂到 IRulesetService 上。
/// 放在 hosted service 里是因为 Initialize 阶段 IRulesetService 可能还未实例化。
/// </summary>
public class RateLimitRuleRegistrar : IHostedService
{
    private readonly IRulesetService _rulesetService;
    private readonly IRateLimitService _rateLimitService;
    private readonly ILogger<RateLimitRuleRegistrar> _logger;

    public RateLimitRuleRegistrar(
        IRulesetService rulesetService,
        IRateLimitService rateLimitService,
        ILogger<RateLimitRuleRegistrar> logger)
    {
        _rulesetService = rulesetService;
        _rateLimitService = rateLimitService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var ruleIds = new[] { Plugin.IntervalRuleId, Plugin.TimePointRuleId, Plugin.TimeRangeRuleId };
        try
        {
            foreach (var id in ruleIds)
            {
                _rulesetService.RegisterRuleHandler(id, Handle);
            }
            _logger.LogInformation("已注册 {Count} 个限频规则处理函数：{Rules}",
                ruleIds.Length, string.Join(", ", ruleIds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册限频规则处理函数失败。期望注册的规则：{Rules}",
                string.Join(", ", ruleIds));
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool Handle(object? rawSettings)
    {
        if (rawSettings is not RateLimitBaseSettings settings)
        {
            _logger.LogWarning(
                "收到非 RateLimitBaseSettings 类型的 settings（实际类型：{Type}），按放行处理。",
                rawSettings?.GetType().FullName ?? "<null>");
            return true;
        }
        var allowed = _rateLimitService.CanExecute(settings, DateTime.Now);
        _logger.LogDebug(
            "限频规则 Handle 调用：模式={Mode}，结果={Result}",
            settings.ModeName, allowed ? "放行" : "拦截");
        return allowed;
    }
}
