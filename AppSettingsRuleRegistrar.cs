using ClassIsland.Core.Abstractions.Services;
using ClassIsland.RateLimit.Models;
using ClassIsland.RateLimit.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClassIsland.RateLimit;

/// <summary>
/// 在应用启动时把"应用设置为"规则的处理函数挂到 IRulesetService 上。
/// 放在 hosted service 里是因为 Initialize 阶段 IRulesetService 可能还未实例化。
/// </summary>
public class AppSettingsRuleRegistrar : IHostedService
{
    private readonly IRulesetService _rulesetService;
    private readonly AppSettingsReader _reader;
    private readonly ILogger<AppSettingsRuleRegistrar> _logger;

    public AppSettingsRuleRegistrar(
        IRulesetService rulesetService,
        AppSettingsReader reader,
        ILogger<AppSettingsRuleRegistrar> logger)
    {
        _rulesetService = rulesetService;
        _reader = reader;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _rulesetService.RegisterRuleHandler(Plugin.AppSettingsRuleId, Handle);
            _logger.LogInformation("已注册\"应用设置为\"规则处理函数：{RuleId}", Plugin.AppSettingsRuleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册\"应用设置为\"规则处理函数失败。规则：{RuleId}", Plugin.AppSettingsRuleId);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool Handle(object? rawSettings)
    {
        if (rawSettings is not AppSettingsRuleSettings settings)
        {
            _logger.LogWarning(
                "收到非 AppSettingsRuleSettings 类型的 settings（实际类型：{Type}），按不满足处理。",
                rawSettings?.GetType().FullName ?? "<null>");
            return false;
        }

        if (string.IsNullOrEmpty(settings.Name))
        {
            _logger.LogDebug("应用设置规则未选择属性，按满足处理。");
            return true;
        }

        var result = _reader.IsCurrentValueEqual(settings.Name, settings.Value);
        _logger.LogDebug(
            "应用设置规则判定：属性={Name}，期望={Expected}，结果={Result}",
            settings.Name, settings.Value, result ? "满足" : "不满足");
        return result;
    }
}