using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;

namespace ClassIsland.RateLimit.Controls;

/// <summary>
/// 按设置属性类型动态选择输入控件的模板选择器。key（模板名）如 <c>.bool</c>、<c>.int</c> 等，
/// 与 <c>AppSettingsRuleSettingsControl</c> 资源里定义的 <c>DataTemplate</c> 一一对应。
/// </summary>
public class AppSettingsRuleControlTemplateSelector : AvaloniaObject, IDataTemplate
{
    [Content]
    public Dictionary<string, IDataTemplate> Templates { get; set; } = new();

    public static readonly DirectProperty<AppSettingsRuleControlTemplateSelector, string>
        ControlTemplateNameProperty =
          AvaloniaProperty.RegisterDirect<AppSettingsRuleControlTemplateSelector, string>(
            nameof(ControlTemplateName), o => o.ControlTemplateName, (o, v) => o.ControlTemplateName = v);

    private string _controlTemplateName = "";
    public string ControlTemplateName
    {
        get => _controlTemplateName;
        set => SetAndRaise(ControlTemplateNameProperty, ref _controlTemplateName, value);
    }

    public Control? Build(object? param = null) => Templates.GetValueOrDefault(ControlTemplateName)?.Build(param);

    public bool Match(object? data) => true;
}