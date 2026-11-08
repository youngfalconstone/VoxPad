using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VoxPad.App.ViewModels;

public static class Converters
{
    public static readonly IValueConverter IsNotNull = new Lambda((v, _) => v != null);
    public static readonly IValueConverter Invert = new Lambda((v, _) => v is bool b ? !b : v);
    public static readonly IValueConverter NonEmptyToVisible = new Lambda((v, _) => string.IsNullOrEmpty(v as string) ? Visibility.Collapsed : Visibility.Visible);
    public static readonly IValueConverter BoolToVisible = new Lambda((v, _) => v is true ? Visibility.Visible : Visibility.Collapsed);

    private sealed class Lambda : IValueConverter
    {
        private readonly Func<object?, object?, object?> _f;
        public Lambda(Func<object?, object?, object?> f) => _f = f;
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => _f(value, parameter);
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b && targetType == typeof(bool) ? !b : Binding.DoNothing;
    }
}
