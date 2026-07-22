using System.Globalization;
using System.Windows.Data;

namespace FamilyTree.App.Localization;

/// <summary>
/// Конвертер: значення-ключ ресурсу → локалізований рядок поточною мовою.
/// Використовується там, де ключ приходить із даних (напр. назви тем у списку),
/// а не заданий статично в XAML через {loc:Localize}.
/// </summary>
public sealed class LocalizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? LocalizationSource.Instance[key] : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
