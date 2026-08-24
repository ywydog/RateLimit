using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.RateLimit.Models;
using ClassIsland.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClassIsland.RateLimit;

/// <summary>
/// 在应用启动时把“当身份认证成功时”规则的处理函数挂到 <see cref="IRulesetService"/> 上。
/// 判定时在 UI 线程弹出认证窗口，用户输入与配置凭据匹配后规则才满足。
/// </summary>
public class AuthorizeSuccessRuleRegistrar : IHostedService
{
    private readonly IRulesetService _rulesetService;
    private readonly ILogger<AuthorizeSuccessRuleRegistrar> _logger;

    public AuthorizeSuccessRuleRegistrar(IRulesetService rulesetService, ILogger<AuthorizeSuccessRuleRegistrar> logger)
    {
        _rulesetService = rulesetService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _rulesetService.RegisterRuleHandler(Plugin.AuthorizeSuccessRuleId, Handle);
            _logger.LogInformation("已注册\"当身份认证成功时\"规则处理函数：{RuleId}", Plugin.AuthorizeSuccessRuleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册\"当身份认证成功时\"规则处理函数失败。规则：{RuleId}", Plugin.AuthorizeSuccessRuleId);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool Handle(object? rawSettings)
    {
        if (rawSettings is not AuthorizeSuccessRuleSettings settings)
        {
            _logger.LogWarning(
                "收到非 AuthorizeSuccessRuleSettings 类型的 settings（实际类型：{Type}），按不满足处理。",
                rawSettings?.GetType().FullName ?? "<null>");
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.CredentialString))
        {
            _logger.LogDebug("身份认证规则未配置凭据，按不满足处理。");
            return false;
        }

        // 认证弹窗必须在 UI 线程运行。若当前不在 UI 线程则调度过去再执行。
        bool result;
        if (Dispatcher.UIThread.CheckAccess())
        {
            result = AuthenticateSynchronously(settings.CredentialString);
        }
        else
        {
            result = Dispatcher.UIThread.Invoke(
                () => AuthenticateSynchronously(settings.CredentialString));
        }

        _logger.LogDebug("身份认证规则判定：凭据已配置，结果={Result}", result ? "满足" : "不满足");
        return result;
    }

    private bool AuthenticateSynchronously(string credentialString)
    {
        try
        {
            var authorizeService = IAppHost.GetService<IAuthorizeService>();

            // ShowDialog 需要 UI 线程持续泵消息才能完成。同步 handler 若直接
            // 阻塞等待该 Task，会死锁。这里用嵌套 DispatcherFrame 潜起消息泵，
            // 直到认证 Task 完成后再取结果，从而实现在同步上下文中等弹窗。
            var frame = new DispatcherFrame();
            var authTask = authorizeService.AuthenticateAsync(credentialString);
            authTask.ContinueWith(_ => frame.Continue = false);
            Dispatcher.UIThread.PushFrame(frame);
            return authTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调用 AuthorizeService 认证时发生异常，按不满足处理。");
            return false;
        }
    }
}