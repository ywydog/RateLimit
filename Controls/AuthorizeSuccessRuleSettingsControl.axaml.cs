using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.RateLimit.Models;

namespace ClassIsland.RateLimit.Controls;

/// <summary>
/// “当身份认证成功时”规则的设置控件，用于编辑用于认证的凭据字符串。
/// </summary>
public partial class AuthorizeSuccessRuleSettingsControl : RuleSettingsControlBase<AuthorizeSuccessRuleSettings>
{
    public AuthorizeSuccessRuleSettingsControl()
    {
        InitializeComponent();
    }
}