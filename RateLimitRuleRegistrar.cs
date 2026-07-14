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
        try
        {
            _rulesetService.RegisterRuleHandler(Plugin.IntervalRuleId, Handle);
            _rulesetService.RegisterRuleHandler(Plugin.TimePointRuleId, Handle);
            _rulesetService.RegisterRuleHandler(Plugin.TimeRangeRuleId, Handle);
            _logger.LogInformation("已注册三个限频规则处理函数。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册限频规则处理函数失败。");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool Handle(object? rawSettings)
    {
        if (rawSettings is not RateLimitBaseSettings settings)
        {
            return true;
        }
        return _rateLimitService.CanExecute(settings, DateTime.Now);
    }
}
