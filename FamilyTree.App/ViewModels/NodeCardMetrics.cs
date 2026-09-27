using FamilyTree.App.Settings;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Розмір картки вузла дерева — обчислений, а не заданий руками.
///
/// Чому обчислений. Картка-підказка сама росте під свій вміст, бо це звичайний
/// <c>Border</c> з <c>Padding</c>. Вузол так не може: розкладка (<c>TreeLayoutEngine</c>)
/// мусить знати розмір ДО того, як WPF щось виміряє, а сітку колонок тримає купи лише
/// однаковий розмір усіх карток. Тож «автоматично» тут означає: розмір виводиться з тих
/// самих чисел, що й вигляд картки — шрифтів, фото й набору ввімкнених рядків.
///
/// Наслідок для користувача той самий, що й у підказки: збільшив шрифт — картка виросла,
/// увімкнув фото — стала ширшою, вимкнув по батькові — нижчою. Нічого не обрізається
/// через те, що хтось забув підкрутити другий повзунок.
/// </summary>
public static class NodeCardMetrics
{
    /// <summary>
    /// Внутрішні поля картки. Збігаються з <c>Margin="10,8"</c> у NodeCardContentTemplate —
    /// числа мусять бути ті самі, інакше обчислена висота розійдеться з намальованою.
    /// </summary>
    private const double PaddingX = 10;
    private const double PaddingY = 8;

    /// <summary>Проміжок між фото й текстом — <c>Margin="0,0,8,0"</c> рамки фото в шаблоні.</summary>
    private const double PhotoGap = 8;

    /// <summary>
    /// Висота рядка відносно кегля. 1.35 — типове для WPF міжрядкове при вирівнюванні
    /// за замовчуванням; на типових 13/11 дає рівно ту картку 160×80, що була зашита
    /// константами до цієї зміни.
    /// </summary>
    private const double LineHeightFactor = 1.35;

    /// <summary>
    /// Ширина текстової колонки відносно основного шрифта. Не вимірюємо справжні імена
    /// навмисно: ширина мала б тоді стрибати при зміні кореня чи глибини (набір імен
    /// інший — картки інші), а одне довге прізвище роздувало б УСІ картки дерева.
    /// Довгі імена, як і раніше, обрізаються через <c>TextTrimming</c>.
    /// Коефіцієнт підібрано так, щоб типовий шрифт 13 дав ті самі 140 пікселів тексту.
    /// </summary>
    private const double TextWidthPerPoint = 10.77;

    /// <summary>Розрахований розмір картки для поточних налаштувань.</summary>
    public static (double Width, double Height) Measure(NodeCardSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var primary = NodeCardSettings.Clamp(
            options.PrimaryFontSize,
            NodeCardSettings.MinPrimaryFontSize,
            NodeCardSettings.MaxPrimaryFontSize,
            NodeCardSettings.DefaultPrimaryFontSize);

        var secondary = NodeCardSettings.Clamp(
            options.SecondaryFontSize,
            NodeCardSettings.MinSecondaryFontSize,
            NodeCardSettings.MaxSecondaryFontSize,
            NodeCardSettings.DefaultSecondaryFontSize);

        var photoHeight = NodeCardSettings.Clamp(
            options.PhotoHeight,
            NodeCardSettings.MinPhotoHeight,
            NodeCardSettings.MaxPhotoHeight,
            NodeCardSettings.DefaultPhotoHeight);

        // Висота — сума видимих рядків. Ім'я показується завжди; решта — за прапорцями.
        // ВАЖЛИВО: рахуємо саме ввімкнені рядки, а не всі можливі, інакше вимкнення
        // по батькові лишало б унизу картки порожнє місце.
        var lines = Line(primary); // ім'я

        if (options.ShowRelationBadge)
        {
            lines += Line(secondary);
        }

        if (options.ShowPatronymic)
        {
            lines += Line(primary);
        }

        if (options.ShowMaidenName)
        {
            lines += Line(secondary);
        }

        if (options.ShowYears)
        {
            lines += Line(secondary);
        }

        var height = lines + (2 * PaddingY);
        var width = (2 * PaddingX) + (primary * TextWidthPerPoint);

        if (options.ShowPhoto)
        {
            width += (photoHeight * NodeCardSettings.PhotoAspect) + PhotoGap;

            // Фото вище за текст — картка тягнеться під фото, а не обрізає його.
            height = Math.Max(height, photoHeight + (2 * PaddingY));
        }

        return (Math.Round(width), Math.Round(height));
    }

    private static double Line(double fontSize) => fontSize * LineHeightFactor;
}
