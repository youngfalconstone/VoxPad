using System.Windows;
using VoxPad.App.Interop;
using VoxPad.Core.Settings;

namespace VoxPad.App.Services;

/// <summary>Light / Dark / Native themes. Native is forced when high contrast or a screen reader is active.</summary>
public static class ThemeService
{
    public static UiTheme Effective { get; private set; } = UiTheme.Light;

    public static bool AccessibilityActive()
    {
        if (SystemParameters.HighContrast) return true;
        bool sr = false;
        try { NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETSCREENREADER, 0, ref sr, 0); } catch { }
        return sr;
    }

    public static void Apply(UiTheme theme)
    {
        if (AccessibilityActive()) theme = UiTheme.Native;
        Effective = theme;
        var dicts = Application.Current.Resources.MergedDictionaries;
        for (int i = dicts.Count - 1; i >= 0; i--)
            if (dicts[i].Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true) dicts.RemoveAt(i);
        var name = theme switch { UiTheme.Dark => "Dark", UiTheme.Native => "Native", _ => "Light" };
        dicts.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/VoxPad;component/Themes/{name}.xaml") });
        dicts.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/VoxPad;component/Themes/Shared.xaml") });
    }
}
