using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace FamilyTree.App.Converters;

/// <summary>
/// Дата в довгому форматі з назвою місяця — для тултіпа <c>DatePicker</c>:
/// українською «9 серпня 1999 р.», англійською «August 9, 1999».
/// <para>
/// Культуру визначає <c>xml:lang</c> елемента (його синхронізує з мовою UI помічник
/// <see cref="Localization.UiLanguage"/> — див. B-01), тож формат завжди відповідає
/// вибраній мові. Береться довгий шаблон дати культури, з якого прибрано день тижня —
/// щоб вивід був однаково стислим в обох мовах (укр-шаблон дня тижня й так не містить,
/// а en-US «dddd, MMMM d, yyyy» стає «MMMM d, yyyy»).
/// </para>
/// Порожня дата → <c>null</c>: тултіп не показується.
/// </summary>
public sealed class LongDateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var date = value switch
        {
            DateTime dt => dt,
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            _ => (DateTime?)null,
        };
        if (date is null)
        {
            return null;
        }

        var specific = ResolveSpecific(culture);
        return date.Value.ToString(LongPatternWithoutWeekday(specific), specific);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    // Форматування дат потребує СПЕЦИФІЧНОЇ культури: мови застосунку (uk/en) нейтральні,
    // а нейтральна культура не має регіону для дат (той самий клас проблеми, що й B-01).
    private static CultureInfo ResolveSpecific(CultureInfo? culture)
    {
        var c = culture ?? CultureInfo.CurrentCulture;
        if (!c.IsNeutralCulture)
        {
            return c;
        }

        try
        {
            return CultureInfo.CreateSpecificCulture(c.Name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.CurrentCulture;
        }
    }

    // Довгий шаблон культури без дня тижня: "dddd, MMMM d, yyyy" → "MMMM d, yyyy".
    private static string LongPatternWithoutWeekday(CultureInfo c)
    {
        var pattern = Regex.Replace(c.DateTimeFormat.LongDatePattern, "dddd|ddd", string.Empty);
        // Прибираємо осиротілі роздільники (коми/пробіли) на краях і подвоєні пробіли.
        pattern = Regex.Replace(pattern, @"^[\s,]+|[\s,]+$", string.Empty);
        return Regex.Replace(pattern, @"\s{2,}", " ");
    }
}
