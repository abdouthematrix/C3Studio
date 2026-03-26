using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using C3Studio.Core.Models;

namespace C3Studio.Converters;

// ── ValidationStatus → Brush ──────────────────────────────────────────────
public class ValidationStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo ci) =>
        (ValidationStatus)value switch
        {
            ValidationStatus.Ok   => new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x94)),
            ValidationStatus.Fail => new SolidColorBrush(Color.FromRgb(0xF0, 0x5C, 0x5C)),
            _                     => new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x80)),
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo ci) => Binding.DoNothing;
}

// ── ValidationStatus → Glyph ──────────────────────────────────────────────
public class ValidationStatusToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo ci) =>
        (ValidationStatus)value switch
        {
            ValidationStatus.Ok   => "✓",
            ValidationStatus.Fail => "✗",
            _                     => "○",
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo ci) => Binding.DoNothing;
}

// ── Category → Background Brush ───────────────────────────────────────────
public class CategoryToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo ci) =>
        (value as string) switch
        {
            "Folder"  => new SolidColorBrush(Color.FromArgb(30, 0x4E, 0xC9, 0x94)),
            "Archive" => new SolidColorBrush(Color.FromArgb(30, 0xC8, 0xA9, 0x6E)),
            "INI"     => new SolidColorBrush(Color.FromArgb(30, 0x88, 0x88, 0xC8)),
            _         => Brushes.Transparent,
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo ci) => Binding.DoNothing;
}

// ── bool → Visibility ─────────────────────────────────────────────────────
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo ci)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo ci)
        => v is Visibility.Visible;
}

// ── !bool → Visibility ────────────────────────────────────────────────────
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo ci)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo ci)
        => v is not Visibility.Visible;
}

// ── 0 → Collapsed, else Visible ───────────────────────────────────────────
public class ZeroToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo ci)
        => value is int n && n == 0 ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo ci) => Binding.DoNothing;
}
