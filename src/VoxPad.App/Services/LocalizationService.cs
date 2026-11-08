using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace VoxPad.App.Services;

/// <summary>UI strings from Strings\Strings.resx (+ satellite assemblies). Language changes take effect after a restart, as in Soundpad.</summary>
public static class Strings
{
    private static readonly ResourceManager Rm = new("VoxPad.App.Strings.Strings", typeof(Strings).Assembly);

    public static readonly (string code, string name)[] Languages =
    {
        ("", "System default"), ("en", "English"), ("de", "Deutsch"), ("fr", "Français"), ("es", "Español"), ("it", "Italiano"),
        ("pt", "Português"), ("pt-BR", "Português (Brasil)"), ("nl", "Nederlands"), ("pl", "Polski"), ("ru", "Русский"), ("uk", "Українська"),
        ("tr", "Türkçe"), ("cs", "Čeština"), ("hu", "Magyar"), ("sv", "Svenska"), ("da", "Dansk"), ("fi", "Suomi"),
        ("ja", "日本語"), ("ko", "한국어"), ("zh-Hans", "中文（简体）"), ("zh-Hant", "中文（繁體）"),
    };

    public static void SetLanguage(string? code)
    {
        if (string.IsNullOrEmpty(code)) return;
        try
        {
            var c = CultureInfo.GetCultureInfo(code);
            CultureInfo.DefaultThreadCurrentUICulture = c;
            Thread.CurrentThread.CurrentUICulture = c;
        }
        catch { }
    }

    public static string Get(string key)
    {
        try { return Rm.GetString(key, CultureInfo.CurrentUICulture) ?? key; }
        catch { return key; }
    }

    /// <summary>Get with a fallback literal (lets the UI render even before resources exist).</summary>
    public static string Get(string key, string fallback)
    {
        try { return Rm.GetString(key, CultureInfo.CurrentUICulture) ?? fallback; }
        catch { return fallback; }
    }

    public static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}

/// <summary>XAML: Text="{s:Str Key=File}" (or {s:Str File}).</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class StrExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public StrExtension() { }
    public StrExtension(string key) => Key = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key, Key);
}
