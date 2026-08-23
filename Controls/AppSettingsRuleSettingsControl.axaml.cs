using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClassIsland.Core;
using ClassIsland.RateLimit.Models;
using ClassIsland.RateLimit.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SettingsInfoAttribute = ClassIsland.Core.Attributes.SettingsInfo;

namespace ClassIsland.RateLimit.Controls;

/// <summary>
/// 配置"应用设置为"规则的控件：抽屉选择应用设置属性 + 按类型动态输入期望值。
/// </summary>
public partial class AppSettingsRuleSettingsControl : RuleSettingsControlBase<AppSettingsRuleSettings>
{
    private const BindingFlags SettingsPropertiesFlags =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    public AppSettingsRuleSettingsControl()
    {
        InitializeComponent();
        _reader = IAppHost.Host?.Services.GetService<AppSettingsReader>() ?? new AppSettingsReader(
            IAppHost.Host?.Services.GetService<ILogger<AppSettingsReader>>()!);
        _logger = IAppHost.Host?.Services.GetService<ILogger<AppSettingsRuleSettingsControl>>();
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
    }

    private readonly AppSettingsReader _reader;
    private readonly ILogger<AppSettingsRuleSettingsControl>? _logger;

    public AppSettingsRuleSettingsControlViewModel ViewModel { get; } = new();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        Settings.PropertyChanged += Settings_OnPropertyChanged;

        UpdateSuggestions();
        if (Settings.Value == null)
            FillCurrentValue();
        if (SetInputValue(Settings.Value))
            UpdateInputer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Settings.PropertyChanged -= Settings_OnPropertyChanged;
        ViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
    }

    void Settings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.Name))
        {
            var prevTemplate = ViewModel.ControlTemplateName;
            UpdateSuggestions();
            if (prevTemplate != ViewModel.ControlTemplateName)
            {
                ViewModel.InputValueLock = true;
                ResetInputer();
                ViewModel.InputValueLock = false;
                FillCurrentValue();
                ViewModel.InputValueLock = true;
                UpdateInputer();
                ViewModel.InputValueLock = false;
            }
        }
    }

    void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.InputValue) && !ViewModel.InputValueLock)
        {
            Settings.Value = ViewModel.InputValue;
        }
    }

    // ---------- 属性枚举 ----------

    List<AppSettingsPropertyInfo>? _properties;
    List<AppSettingsPropertyInfo> Properties => _properties ??= EnumerateProperties();

    List<AppSettingsPropertyInfo> EnumerateProperties()
    {
        var settingsType = _reader.GetSettingsType();
        if (settingsType is null)
        {
            _logger?.LogWarning("无法反射主程序 Settings 类型，应用设置属性列表为空。");
            return new List<AppSettingsPropertyInfo>();
        }

        var list = new List<AppSettingsPropertyInfo>();
        foreach (var property in settingsType.GetProperties(SettingsPropertiesFlags))
        {
            if (property.GetCustomAttribute<ObsoleteAttribute>() != null) continue;
            var settingsInfo = property.GetCustomAttribute<SettingsInfoAttribute>();
            var enums = settingsInfo?.Enums ??
                        (property.PropertyType.IsEnum
                            ? Enum.GetNames(property.PropertyType)
                            : null);
            list.Add(new AppSettingsPropertyInfo
            {
                Name = settingsInfo?.Name ?? property.Name,
                Glyph = settingsInfo?.Glyph ?? "\uE7C9",
                PropertyName = property.Name,
                Type = property.PropertyType,
                Enums = enums,
                Order = settingsInfo?.Order ?? 10,
                PreviewValue = GetPreviewValue(property)
            });
        }

        return list
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Name)
            .ToList();
    }

    string GetPreviewValue(PropertyInfo property)
    {
        var value = _reader.GetValue(property.Name);
        return value switch
        {
            null => "[null]",
            bool b => b ? "开" : "关",
            double d => d.ToString("0.0###"),
            decimal m => m.ToString("0.0###"),
            _ => ConvertToPreview(value)
        };

        string ConvertToPreview(object v)
        {
            var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (IsSupportedType(underlying) || underlying.IsEnum)
                return v.ToString() ?? "";
            return JsonSerializer.Serialize(v, AppSettingsJsonOptions.FriendlyJsonSerializerOptions);
        }
    }

    static bool IsSupportedType(Type type) =>
        type == typeof(string) || type == typeof(int) || type == typeof(double) ||
        type == typeof(bool) || type == typeof(Color);

    static Type GetUnderlyingType(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    bool IsPropertySupported(AppSettingsPropertyInfo item)
    {
        var type = GetUnderlyingType(item.Type);
        return IsSupportedType(type) || type.IsEnum || item.Enums != null;
    }

    // ---------- 抽屉选择属性 ----------

    void SelectorButton_OnClick(object? sender, RoutedEventArgs e)
    {
        UpdateSearchResults();
        ShowPropertyPicker();
    }

    void SearchTextBox_OnTextChanged(object? sender, TextChangedEventArgs e) =>
        UpdateSearchResults((sender as TextBox)?.Text);

    async void ShowPropertyPicker()
    {
        var panel = PropertyPickerPanel;
        panel.DataContext = this;
        var dialog = new ContentDialog
        {
            Content = panel,
            Title = "选择应用设置",
            PrimaryButtonText = "确定",
            DefaultButton = ContentDialogButton.Primary,
        };
        await dialog.ShowAsync(TopLevel.GetTopLevel(this));
    }

    Control PropertyPickerPanel => _propertyPickerPanel ??=
        (Control)this.FindResource("PropertyPickerPanel")!;

    private Control? _propertyPickerPanel;

    void UpdateSearchResults(string? keyword = null)
    {
        var kw = keyword?.Trim();
        ViewModel.SearchResults = string.IsNullOrEmpty(kw)
            ? Properties.Where(IsPropertySupported).ToList()
            : Properties.Where(IsPropertySupported)
                .Where(item => item.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                               item.PropertyName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    void PropertyPickerList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is AppSettingsPropertyInfo info)
        {
            Settings.Name = info.PropertyName;
        }
    }

    // ---------- 控件模板选择 ----------

    AppSettingsRuleControlTemplateSelector? _templateSelector;
    AppSettingsRuleControlTemplateSelector TemplateSelector => _templateSelector ??=
        (AppSettingsRuleControlTemplateSelector)this.FindResource("AppSettingsTemplateSelector")!;

    void ResetInputer()
    {
        TemplateSelector.ControlTemplateName = "";
        ViewModel.IsInContentPresenter2 = false;
        PropertyInputer1.Content = null;
        PropertyInputer2.Content = null;
    }

    void UpdateInputer()
    {
        if (ViewModel.CurrentPropertyInfo?.Type == null) return;

        ResetInputer();
        TemplateSelector.ControlTemplateName = ViewModel.ControlTemplateName;

        ViewModel.IsInContentPresenter2 =
            ViewModel.ControlTemplateName == ".string" &&
            Settings.Value?.ToString()?.Length > 20;

        if (ViewModel.IsInContentPresenter2)
            PropertyInputer2.Content = TemplateSelector.Build();
        else
            PropertyInputer1.Content = TemplateSelector.Build();
    }

    void UpdateSuggestions()
    {
        if (string.IsNullOrEmpty(Settings.Name))
        {
            ViewModel.CurrentPropertyInfo = null;
            ViewModel.ControlTemplateName = "";
            return;
        }

        ViewModel.CurrentPropertyInfo = Properties.FirstOrDefault(p => p.PropertyName == Settings.Name);
        ViewModel.ControlTemplateName = DetermineControlType(ViewModel.CurrentPropertyInfo);
    }

    string DetermineControlType(AppSettingsPropertyInfo? info)
    {
        if (info == null) return "";
        if (info.Enums != null) return ".enums";
        var type = GetUnderlyingType(info.Type);
        if (type == typeof(bool)) return ".bool";
        if (type == typeof(int)) return ".int";
        if (type == typeof(double)) return ".double";
        if (type == typeof(Color)) return ".color";
        return ".string";
    }

    // ---------- 填充当前值 ----------

    void FillCurrentValueButton_OnClick(object? sender, RoutedEventArgs e) => FillCurrentValue();

    void FillCurrentValue()
    {
        if (string.IsNullOrEmpty(Settings.Name)) return;
        ViewModel.InputValueLock = true;
        SetInputValue(_reader.GetValue(Settings.Name));
        ViewModel.InputValueLock = false;
    }

    bool SetInputValue(object? value)
    {
        var editable = ConvertToEditableType(value);
        if (editable != null)
        {
            ViewModel.InputValue = editable;
            Settings.Value = editable;
            return true;
        }
        return false;
    }

    object? ConvertToEditableType(object? value)
    {
        if (value == null) return null;
        try
        {
            switch (ViewModel.ControlTemplateName)
            {
                case ".string":
                    return value switch
                    {
                        string str => str,
                        JsonElement json => json.GetString(),
                        _ => JsonSerializer.Serialize(value, AppSettingsJsonOptions.FriendlyJsonSerializerOptions)
                    };
                case ".enums":
                    return value switch
                    {
                        int i => i,
                        JsonElement json => json.Deserialize<int>(),
                        Enum e => Convert.ToInt32(e),
                        _ => Convert.ToInt32(value)
                    };
                case ".color":
                    return value switch
                    {
                        Color c => c,
                        string str => TryParseColor(str),
                        JsonElement json => json.Deserialize<Color>(),
                        _ => value
                    };
                case ".int":
                    return value switch
                    {
                        int i => i,
                        double d => (int)d,
                        JsonElement json => json.Deserialize<int>(),
                        _ => Convert.ToInt32(value)
                    };
                case ".double":
                    return value switch
                    {
                        double d => d,
                        int i => (double)i,
                        JsonElement json => json.Deserialize<double>(),
                        _ => Convert.ToDouble(value)
                    };
            }

            if (ViewModel.CurrentPropertyInfo?.Type is { } propertyType && value is JsonElement jsonElement)
                return jsonElement.Deserialize(propertyType, AppSettingsJsonOptions.FriendlyJsonSerializerOptions);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "转换期望值到可编辑类型失败：{Value}", value);
        }
        return value;
    }

    static Color? TryParseColor(string s)
    {
        Color c;
        if (Color.TryParse(s, out c)) return c;
        if (Color.TryParse(s.TrimStart('#'), out c)) return c;
        return null;
    }
}

/// <summary>
/// "应用设置为"规则设置的 ViewModel。
/// </summary>
public partial class AppSettingsRuleSettingsControlViewModel : ObservableObject
{
    internal bool InputValueLock;

    private object _inputValue = "[未初始化]";
    public object InputValue
    {
        get => _inputValue;
        set
        {
            if (value == null || value.Equals(_inputValue)) return;
            if (InputValueLock) return;
            _inputValue = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private string _controlTemplateName = "";
    [ObservableProperty] private AppSettingsPropertyInfo? _currentPropertyInfo;
    [ObservableProperty] private List<AppSettingsPropertyInfo> _searchResults = new();
    [ObservableProperty] private bool _isInContentPresenter2;
}

/// <summary>
/// 一个可枚举的应用设置属性条目。
/// </summary>
public class AppSettingsPropertyInfo
{
    public string Name { get; init; } = "";
    public string Glyph { get; init; } = "\uE7C9";
    public string PropertyName { get; init; } = "";
    public Type Type { get; init; } = null!;
    public string[]? Enums { get; init; }
    public double Order { get; init; } = 10;
    public string PreviewValue { get; init; } = "";

    public string Display => string.IsNullOrEmpty(Name) ? PropertyName : $"{Name}（{PropertyName}）";
}