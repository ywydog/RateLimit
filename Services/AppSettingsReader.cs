using System.Reflection;
using System.Text.Json;
using Avalonia.Media;
using ClassIsland.Core;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClassIsland.RateLimit.Services;

/// <summary>
/// 读取 ClassIsland 主程序应用设置的辅助服务。
/// </summary>
/// <remarks>
/// 插件只引用 <c>ClassIsland.Core</c>/<c>Shared</c>，而 <c>SettingsService</c>、<c>Settings</c>
/// 都在主程序程序集 <c>ClassIsland</c> 中，无法编译期引用。因此运行时通过反射访问：
/// <code>
/// AppBase.Current.MainWindow.GetType().Assembly            // 定位主程序集
///     .GetType("ClassIsland.Services.SettingsService")     // 拿到服务类型
/// IAppHost.Host.Services.GetService(type)                  // 从 DI 取单例
///     .GetProperty("Settings").GetValue(...)               // 镜像 Settings 对象
/// </code>
/// 该方案参考了其他 ClassIsland 插件（SystemTools 等）的既有做法。
/// </remarks>
public class AppSettingsReader
{
    private const string SettingsServiceTypeName = "ClassIsland.Services.SettingsService";

    private readonly ILogger<AppSettingsReader> _logger;

    public AppSettingsReader(ILogger<AppSettingsReader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 反射定位主程序 <c>SettingsService.Settings</c> 的 <see cref="PropertyInfo"/> 列表。
    /// 返回主程序 <c>Settings</c> 类型；找不到主程序时返回 null，此时控件应显示为空。
    /// </summary>
    public Type? GetSettingsType()
    {
        try
        {
            var assembly = AppBase.Current.MainWindow?.GetType().Assembly;
            var settingsServiceType = assembly?.GetType(SettingsServiceTypeName);
            var settingsType = settingsServiceType?
                .GetProperty("Settings", BindingFlags.Instance | BindingFlags.Public)?
                .PropertyType;
            return settingsType is null ? null : settingsType;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "反射定位主程序 Settings 类型失败。");
            return null;
        }
    }

    /// <summary>
    /// 读取指定应用设置属性的当前值。属性不存在或读取失败时返回 null。
    /// </summary>
    public object? GetValue(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName)) return null;
        try
        {
            var assembly = AppBase.Current.MainWindow?.GetType().Assembly;
            var settingsServiceType = assembly?.GetType(SettingsServiceTypeName);
            if (settingsServiceType is null) return null;

            var settingsService = IAppHost.Host?.Services.GetService(settingsServiceType);
            var settings = settingsServiceType
                .GetProperty("Settings", BindingFlags.Instance | BindingFlags.Public)?
                .GetValue(settingsService);
            var property = settings?.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            return property?.CanRead == true ? property.GetValue(settings) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取应用设置属性 {PropertyName} 失败。", propertyName);
            return null;
        }
    }

    /// <summary>
    /// 读取当前值并转换为可比较的类型，与规则设置的期望值做比较。
    /// 支持支持类型（string/int/double/bool/Color/枚举/其他 Json 类型）。
    /// </summary>
    /// <returns>当前值==期望值时返回 true；任何无法比较或解析失败时返回 false。</returns>
    public bool IsCurrentValueEqual(string propertyName, object? expected)
    {
        var current = GetValue(propertyName);
        if (current is null || expected is null)
        {
            return current is null && expected is null;
        }

        var expectedConverted = ConvertExpected(expected, current.GetType());
        if (expectedConverted is null)
        {
            // 转换失败可能是反序列化问题，但不等于——返回 false 保守处理。
            _logger.LogTrace(
                "AppSettings 期望值转换失败：属性={PropertyName}，期望类型={ExpectedType}，目标类型={TargetType}",
                propertyName, expected.GetType().Name, current.GetType().Name);
            return false;
        }

        return Equals(current, expectedConverted);
    }

    private static object? ConvertExpected(object expected, Type targetType)
    {
        targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (expected.GetType() == targetType) return expected;

        var friendly = AppSettingsJsonOptions.FriendlyJsonSerializerOptions;

        if (targetType == typeof(bool))
        {
            return expected switch
            {
                bool b => b,
                JsonElement json => json.GetBoolean(),
                string str when bool.TryParse(str, out var b) => b,
                _ => null
            };
        }
        if (targetType == typeof(int))
        {
            return expected switch
            {
                int i => i,
                long l => (int)l,
                double d => (int)d,
                JsonElement json => json.Deserialize<int>(friendly),
                string str when int.TryParse(str, out var i) => i,
                _ => Convert.ToInt32(expected)
            };
        }
        if (targetType == typeof(double))
        {
            return expected switch
            {
                double d => d,
                int i => i,
                JsonElement json => json.GetDouble(),
                _ => Convert.ToDouble(expected)
            };
        }
        if (targetType == typeof(string))
        {
            return expected switch
            {
                string s => s,
                JsonElement json => json.GetString(),
                _ => expected.ToString()
            };
        }
        if (targetType == typeof(Color))
        {
            return expected switch
            {
                Color c => c,
                string str => TryParseColor(str),
                JsonElement json => json.GetString() is { } s ? TryParseColor(s) : null,
                _ => null
            };
        }
        if (targetType.IsEnum)
        {
            return expected switch
            {
                JsonElement json => json.ValueKind == JsonValueKind.String
                    ? Enum.Parse(targetType, json.GetString()!)
                    : Enum.ToObject(targetType, json.GetInt32()),
                string str => Enum.Parse(targetType, str),
                _ => Enum.ToObject(targetType, Convert.ToInt32(expected))
            };
        }

        // 其他复杂类型：尝试用 JsonElement / 字符串反序列化。
        try
        {
            if (expected is JsonElement json) return json.Deserialize(targetType, friendly);
            if (expected is string s) return JsonSerializer.Deserialize(s, targetType, friendly);
            return Convert.ChangeType(expected, targetType);
        }
        catch
        {
            return null;
        }
    }

    private static Color? TryParseColor(string s)
    {
        Color c;
        if (Color.TryParse(s, out c)) return c;
        if (Color.TryParse(s.TrimStart('#'), out c)) return c;
        return null;
    }
}

/// <summary>
/// JSON 序列化选项：与主程序 ModifyAppSettingsAction 的友好选项保持一致，
/// 保证 JsonElement/字符串双向转换行为一致。
/// </summary>
public static class AppSettingsJsonOptions
{
    public static readonly JsonSerializerOptions FriendlyJsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };
}

