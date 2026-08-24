using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.RateLimit.Models;

/// <summary>
/// “当身份认证成功时”规则设置：保存用于认证的凭据字符串。
/// </summary>
public class AuthorizeSuccessRuleSettings : ObservableRecipient
{
    private string _credentialString = "";

    /// <summary>
    /// 要用于认证的凭据字符串。可通过 ClassIsland 的凭据编辑控件创建/修改。
    /// 判定规则时若已配置凭据，会弹出认证窗口，用户输入匹配的凭据后规则才满足。
    /// </summary>
    public string CredentialString
    {
        get => _credentialString;
        set
        {
            if (value == _credentialString) return;
            _credentialString = value;
            OnPropertyChanged();
        }
    }
}