namespace FamilyTree.Domain;

/// <summary>Календар дати. Підтримуються лише григоріанський і юліанський (T-5.7).</summary>
public enum DateCalendar
{
    Gregorian,
    Julian,
}

/// <summary>Точність дати-точки: до дня / до місяця / до року.</summary>
public enum DatePrecision
{
    Year,
    Month,
    Day,
}

/// <summary>Кваліфікатор приблизної дати (GEDCOM ABT/CAL/EST).</summary>
public enum DateApproximation
{
    /// <summary>«близько» — GEDCOM ABT.</summary>
    About,

    /// <summary>«обчислено» — GEDCOM CAL.</summary>
    Calculated,

    /// <summary>«оцінка» — GEDCOM EST.</summary>
    Estimated,
}

/// <summary>Різновид діапазону (GEDCOM BEF/AFT/BET…AND).</summary>
public enum DateRangeKind
{
    /// <summary>«до» — GEDCOM BEF.</summary>
    Before,

    /// <summary>«після» — GEDCOM AFT.</summary>
    After,

    /// <summary>«між … і» — GEDCOM BET…AND.</summary>
    Between,
}

/// <summary>Форма значення <see cref="FamilyDate"/>.</summary>
public enum FamilyDateKind
{
    /// <summary>Точна або часткова дата (одна точка; точність — від дня до року).</summary>
    Exact,

    /// <summary>Приблизна дата (точка + <see cref="DateApproximation"/>).</summary>
    Approximate,

    /// <summary>Діапазон (до / після / між).</summary>
    Range,

    /// <summary>Лише вільний текст (нерозпарсована дата).</summary>
    Phrase,
}

/// <summary>
/// Одна календарна дата-точка змінної точності. Рік обов'язковий; місяць і день — опційні
/// (їх відсутність і задає <see cref="Precision"/>). Роки до н.е. не підтримуються (T-5.7).
/// </summary>
public sealed record DatePoint
{
    /// <summary>Рік (обов'язковий, ≥ 1).</summary>
    public required int Year { get; init; }

    /// <summary>Місяць 1..12 або null.</summary>
    public int? Month { get; init; }

    /// <summary>День 1..31 або null (лише коли задано <see cref="Month"/>).</summary>
    public int? Day { get; init; }

    /// <summary>Календар точки.</summary>
    public DateCalendar Calendar { get; init; } = DateCalendar.Gregorian;

    /// <summary>Точність, виведена з наявності місяця/дня.</summary>
    public DatePrecision Precision =>
        Day is not null ? DatePrecision.Day
        : Month is not null ? DatePrecision.Month
        : DatePrecision.Year;

    /// <summary>Структурно коректна: рік ≥ 1; місяць у 1..12; день лише з місяцем.</summary>
    public bool IsValid
    {
        get
        {
            if (Year < 1)
            {
                return false;
            }

            if (Month is { } m && (m < 1 || m > 12))
            {
                return false;
            }

            if (Day is { } d && (Month is null || d < 1 || d > 31))
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>Точка з повної григоріанської дати.</summary>
    public static DatePoint FromDateOnly(DateOnly date) => new()
    {
        Year = date.Year,
        Month = date.Month,
        Day = date.Day,
        Calendar = DateCalendar.Gregorian,
    };

    /// <summary>
    /// Представницька <see cref="DateOnly"/> для сортування/порівнянь: відсутні місяць/день
    /// заповнюються одиницею; календар ігнорується (для порядку різниця в ~13 днів неістотна).
    /// Повертає null, якщо точку не можна привести до коректної дати.
    /// </summary>
    public DateOnly? ToDateOnly()
    {
        var month = Month ?? 1;
        var day = Day ?? 1;

        if (Year is < 1 or > 9999 || month is < 1 or > 12 || day < 1)
        {
            return null;
        }

        try
        {
            return new DateOnly(Year, month, Math.Min(day, DateTime.DaysInMonth(Year, month)));
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Значення дати з підтримкою неточності (T-5.7): точна/часткова, приблизна, діапазон, фраза.
/// Незмінний value-об'єкт (рівність — за значенням). <c>null</c> у полі-власнику означає
/// «дата невідома». Заміняє попередній <see cref="DateOnly"/>? у полях дат.
/// </summary>
public sealed record FamilyDate
{
    /// <summary>Форма значення.</summary>
    public required FamilyDateKind Kind { get; init; }

    /// <summary>
    /// Основна точка: сама дата для Exact/Approximate; для діапазону — межа
    /// (нижня «from» для Between; сама межа для Before/After). Null лише для Phrase.
    /// </summary>
    public DatePoint? Start { get; init; }

    /// <summary>Верхня межа «to» — лише для <see cref="DateRangeKind.Between"/>.</summary>
    public DatePoint? End { get; init; }

    /// <summary>Кваліфікатор — лише для <see cref="FamilyDateKind.Approximate"/>.</summary>
    public DateApproximation? Approximation { get; init; }

    /// <summary>Різновид — лише для <see cref="FamilyDateKind.Range"/>.</summary>
    public DateRangeKind? RangeKind { get; init; }

    /// <summary>Текст — лише для <see cref="FamilyDateKind.Phrase"/>.</summary>
    public string? Phrase { get; init; }

    /// <summary>
    /// Сирий GEDCOM-вираз дати з імпорту (для точного зворотного експорту). Заповнюється лише
    /// при імпорті, у UI не показується; при ручному редагуванні дати має скидатися в null.
    /// </summary>
    public string? OriginalGedcom { get; init; }

    // ---- Фабрики -------------------------------------------------------------

    /// <summary>Точна або часткова дата з готової точки.</summary>
    public static FamilyDate Exact(DatePoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return new FamilyDate { Kind = FamilyDateKind.Exact, Start = point };
    }

    /// <summary>Точна дата з повної григоріанської <see cref="DateOnly"/>.</summary>
    public static FamilyDate Exact(DateOnly date) => Exact(DatePoint.FromDateOnly(date));

    /// <summary>
    /// Неявне перетворення повної дати на <see cref="FamilyDateKind.Exact"/>. Дозволяє коду,
    /// що працював із <see cref="DateOnly"/> (домен, сидер, тести), лишитися без змін — C#
    /// піднімає це перетворення й до <c>DateOnly?</c> → <c>FamilyDate?</c> (T-5.2a).
    /// </summary>
    public static implicit operator FamilyDate(DateOnly date) => Exact(date);

    /// <summary>Приблизна дата (точка + кваліфікатор).</summary>
    public static FamilyDate Approximate(DateApproximation approximation, DatePoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return new FamilyDate
        {
            Kind = FamilyDateKind.Approximate,
            Approximation = approximation,
            Start = point,
        };
    }

    /// <summary>Діапазон «до» (GEDCOM BEF).</summary>
    public static FamilyDate Before(DatePoint point) => Range(DateRangeKind.Before, point, null);

    /// <summary>Діапазон «після» (GEDCOM AFT).</summary>
    public static FamilyDate After(DatePoint point) => Range(DateRangeKind.After, point, null);

    /// <summary>Діапазон «між … і» (GEDCOM BET…AND). Порядок from/to не перевіряється (м'яке правило UI).</summary>
    public static FamilyDate Between(DatePoint from, DatePoint to)
    {
        ArgumentNullException.ThrowIfNull(to);
        return Range(DateRangeKind.Between, from, to);
    }

    /// <summary>Лише текстова (нерозпарсована) дата.</summary>
    public static FamilyDate FromPhrase(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new FamilyDate { Kind = FamilyDateKind.Phrase, Phrase = text };
    }

    private static FamilyDate Range(DateRangeKind rangeKind, DatePoint start, DatePoint? end)
    {
        ArgumentNullException.ThrowIfNull(start);
        return new FamilyDate
        {
            Kind = FamilyDateKind.Range,
            RangeKind = rangeKind,
            Start = start,
            End = end,
        };
    }

    /// <summary>Копія з проставленим сирим GEDCOM-виразом (round-trip при імпорті).</summary>
    public FamilyDate WithOriginalGedcom(string? gedcom) => this with { OriginalGedcom = gedcom };

    // ---- Похідні члени -------------------------------------------------------

    /// <summary>Представницький рік для сортування/показу; null для Phrase.</summary>
    public int? EffectiveYear => Kind == FamilyDateKind.Phrase ? null : Start?.Year;

    /// <summary>
    /// Представницька <see cref="DateOnly"/> для сортування й м'яких попереджень (вік,
    /// смерть&lt;народження). Бере основну точку; для Phrase (і нерезолвних точок) — null,
    /// і тоді відповідне порівняння просто пропускається.
    /// </summary>
    public DateOnly? ToComparable() =>
        Kind == FamilyDateKind.Phrase ? null : Start?.ToDateOnly();

    /// <summary>Структурна коректність за формою (для перевірки при завантаженні файлу).</summary>
    public bool IsStructurallyValid() => Kind switch
    {
        FamilyDateKind.Exact => Start is { IsValid: true },
        FamilyDateKind.Approximate => Approximation is not null && Start is { IsValid: true },
        FamilyDateKind.Range => RangeKind is { } rk
            && Start is { IsValid: true }
            && (rk == DateRangeKind.Between ? End is { IsValid: true } : End is null),
        FamilyDateKind.Phrase => !string.IsNullOrWhiteSpace(Phrase),
        _ => false,
    };
}
