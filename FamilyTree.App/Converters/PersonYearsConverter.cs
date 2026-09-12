using System.Globalization;
using System.Windows.Data;
using FamilyTree.Domain;

namespace FamilyTree.App.Converters;

/// <summary>
/// Роки життя особи для списку: «1954–2020», «1988» (лише народження) тощо.
/// Мовонезалежний (тільки числа), тож не потребує оновлення при зміні мови.
/// Неточну дату представляє її роком (<see cref="FamilyDate.EffectiveYear"/>).
/// </summary>
public sealed class PersonYearsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Person person)
        {
            return string.Empty;
        }

        var birth = person.Birth?.Date?.EffectiveYear?.ToString(CultureInfo.InvariantCulture);
        var death = person.Death?.Date?.EffectiveYear?.ToString(CultureInfo.InvariantCulture);

        return (birth, death) switch
        {
            (null, null) => string.Empty,

            // Відкритий кінець «1980–»: помер, рік невідомий (Person.Deceased).
            (not null, null) => person.IsAlive ? birth! : $"{birth}–",

            (null, not null) => $"–{death}",
            _ => $"{birth}–{death}",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
