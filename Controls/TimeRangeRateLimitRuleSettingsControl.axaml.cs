using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.RateLimit.Models;

namespace ClassIsland.RateLimit.Controls;

/// <summary>
/// TimeRangeRateLimitRuleSettingsControl.xaml 的交互逻辑
/// </summary>
public partial class TimeRangeRateLimitRuleSettingsControl : RuleSettingsControlBase<TimeRangeRateLimitSettings>
{
    public TimeRangeRateLimitRuleSettingsControl()
    {
        InitializeComponent();
    }
}
