using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.RateLimit.Services;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClassIsland.RateLimit;

/// <summary>
/// "记录限频执行" 行动。放在工作流末尾，让服务在动作真执行后再记一次。
/// 这样可以避免 Rule 端默认记录 + Action 端再次记录造成的重复计数（如果用户同时配了规则 + 行动）。
/// </summary>
[ActionInfo(Plugin.RecordActionId, "记录限频执行", "\uE916", addDefaultToMenu: false)]
public class RateLimitRecordAction : ActionBase
{
    private readonly ILogger<RateLimitRecordAction> _logger;

    public RateLimitRecordAction(ILogger<RateLimitRecordAction> logger)
    {
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        var service = IAppHost.Host?.Services.GetService<IRateLimitService>();
        if (service is null)
        {
            _logger.LogError("RecordAction：无法解析 IRateLimitService，本次执行未记录。");
            return;
        }

        var workflowId = ActionSet.Guid;
        service.Record(workflowId, DateTime.Now);
        _logger.LogDebug("RecordAction：已通知服务记录一次执行。工作流={WorkflowId}", workflowId);
    }
}
