using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.RateLimit.Models;

/// <summary>
/// "应用设置为"规则设置：判断某个应用设置属性的当前值是否等于指定的期望值。
/// </summary>
public class AppSettingsRuleSettings : ObservableRecipient
{
    private string _name = "";
    /// <summary>
    /// 要检查的应用设置属性名（如 <c>IsMainWindowVisible</c>、<c>Theme</c>）。
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (value == _name) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 期望值。从配置反序列化时可能是 <see cref="System.Text.Json.JsonElement"/>，
    /// 判定时需按目标属性类型转换后再与原值比较。
    /// </summary>
    public object? Value { get; set; }
}