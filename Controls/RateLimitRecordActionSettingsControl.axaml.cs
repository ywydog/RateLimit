using ClassIsland.Core.Abstractions.Controls;

namespace ClassIsland.RateLimit.Controls;

/// <summary>
/// "记录限频执行" 行动无需任何设置，本控件作为占位显示。
/// </summary>
public partial class RateLimitRecordActionSettingsControl : ActionSettingsControlBase
{
    public RateLimitRecordActionSettingsControl()
    {
        InitializeComponent();
    }
}
