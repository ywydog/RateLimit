using ClassIsland.RateLimit.Models;

namespace ClassIsland.RateLimit.Services;

/// <summary>
/// 限频服务接口。负责记录每个工作流的执行历史并判断是否允许放行。
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// 判断当前是否允许执行。允许时同时记录一次执行，返回 true；不允许返回 false。
    /// </summary>
    /// <param name="settings">限频规则设置（必为 <see cref="RateLimitBaseSettings"/> 的派生实例）。</param>
    /// <param name="now">当前时间。</param>
    bool CanExecute(RateLimitBaseSettings settings, DateTime now);

    /// <summary>
    /// 仅记录一次执行到指定工作流的历史（不动判断逻辑）。给"动作真执行了再记"的场景用。
    /// </summary>
    /// <param name="workflowId">目标工作流 GUID。</param>
    /// <param name="now">当前时间。</param>
    void Record(Guid workflowId, DateTime now);

    /// <summary>
    /// 清除指定工作流的历史计数。
    /// </summary>
    void Reset(Guid workflowId);

    /// <summary>
    /// 获取指定工作流当前历史记录条数（调试用）。
    /// </summary>
    int GetCount(Guid workflowId);
}
