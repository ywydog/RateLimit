using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.RateLimit.Controls;
using ClassIsland.RateLimit.Models;
using ClassIsland.RateLimit.Services;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClassIsland.RateLimit;

/// <summary>
/// 限频规则插件入口。注册三个独立规则和一个可选记录行动。
/// </summary>
[PluginEntrance]
public class Plugin : PluginBase
{
    /// <summary>插件 ID，需与 manifest.yml 的 id 保持一致。</summary>
    public const string PluginId = "classisland.plugin.ratelimit";

    public const string IntervalRuleId = PluginId + ".interval";
    public const string TimePointRuleId = PluginId + ".timePoint";
    public const string TimeRangeRuleId = PluginId + ".timeRange";
    public const string RecordActionId = PluginId + ".record";

    private ILogger<Plugin>? _logger;

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // Plugin 由 ClassIsland 直接 new，不走 DI，所以这里延迟取 logger。
        _logger = IAppHost.Host?.Services.GetService<ILogger<Plugin>>();
        _logger?.LogInformation("ClassIsland.RateLimit 插件开始初始化（版本 {Version}）",
            Info?.Manifest?.Version ?? "<unknown>");

        // 1. 注册服务
        services.AddSingleton<IRateLimitService, RateLimitService>();

        // 2. 注册三条独立规则
        services.AddRule<IntervalRateLimitSettings, IntervalRateLimitRuleSettingsControl>(
            IntervalRuleId, "限频：时间间隔", "\uE916");
        services.AddRule<TimePointRateLimitSettings, TimePointRateLimitRuleSettingsControl>(
            TimePointRuleId, "限频：时间点", "\uE916");
        services.AddRule<TimeRangeRateLimitSettings, TimeRangeRateLimitRuleSettingsControl>(
            TimeRangeRuleId, "限频：时间段", "\uE916");

        // 3. 注册"记录限频执行"行动
        services.AddAction<RateLimitRecordAction, RateLimitRecordActionSettingsControl>();

        // 4. 启动时把三条规则的 Handle 挂到 IRulesetService
        services.AddHostedService<RateLimitRuleRegistrar>();

        _logger?.LogInformation(
            "ClassIsland.RateLimit 插件初始化完成：已注册 3 条规则（{Interval} / {TimePoint} / {TimeRange}）与 1 个行动（{Record}）",
            IntervalRuleId, TimePointRuleId, TimeRangeRuleId, RecordActionId);
    }
}
