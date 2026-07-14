using System.Text.Json.Serialization;
using ClassIsland.RateLimit.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.RateLimit.Models;

/// <summary>
/// 限频规则设置基类。三个模式各自有派生类，共享最大次数、时间窗口、缓存的工作流 GUID。
/// </summary>
public abstract class RateLimitBaseSettings : ObservableObject
{
    /// <summary>
    /// 时间窗口内允许的最大执行次数。
    /// </summary>
    [ObservableProperty]
    private int _maxCount = 3;

    /// <summary>
    /// 限频时间窗口（秒）。窗口外的历史记录将被丢弃。
    /// </summary>
    [ObservableProperty]
    private int _windowSeconds = 600;

    /// <summary>
    /// 内部运行时标记：当前 settings 实例所属工作流的 GUID。
    /// 由 <see cref="RateLimitService"/> 在惰性反查时填入，不参与序列化。
    /// </summary>
    [JsonIgnore]
    internal Guid? CachedWorkflowId { get; set; }

    /// <summary>
    /// 由派生类实现：判断当前时间是否处于本模式的"允许窗口"。
    /// 返回 true 表示当前时间在模式语义内，false 表示不在（例如不在时间段内、没到时间点）。
    /// </summary>
    /// <param name="now">当前时间。</param>
    /// <param name="workflowId">本 settings 所属工作流 GUID。</param>
    /// <param name="service">服务实例，供派生类加载历史使用。</param>
    internal abstract bool IsInMode(DateTime now, Guid workflowId, RateLimitService service);
}
