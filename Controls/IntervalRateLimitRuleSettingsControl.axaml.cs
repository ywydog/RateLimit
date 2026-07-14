using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.RateLimit.Models;

namespace ClassIsland.RateLimit.Controls;

/// <summary>
/// IntervalRateLimitRuleSettingsControl.xaml 的交互逻辑
/// </summary>
public partial class IntervalRateLimitRuleSettingsControl : RuleSettingsControlBase<IntervalRateLimitSettings>
{
    public IntervalRateLimitRuleSettingsControl()
    {
        InitializeComponent();
    }
}
