using System.Globalization;
using System.Windows.Data;
using FamilyTree.App.Localization;

namespace FamilyTree.App.Converters;

/// <summary>Позначка глибини: 0 → локалізоване «Усі», інакше — число поколінь.</summary>
public sealed class DepthLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int depth && depth > 0
            ? depth.ToString(CultureInfo.CurrentCulture)
            : LocalizationSource.Instance["Tree_AllDepths"];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
