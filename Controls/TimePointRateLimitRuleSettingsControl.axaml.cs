using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.RateLimit.Models;

namespace ClassIsland.RateLimit.Controls;

/// <summary>
/// TimePointRateLimitRuleSettingsControl.xaml 的交互逻辑
/// </summary>
public partial class TimePointRateLimitRuleSettingsControl : RuleSettingsControlBase<TimePointRateLimitSettings>
{
    public TimePointRateLimitRuleSettingsControl()
    {
        InitializeComponent();
    }
}
