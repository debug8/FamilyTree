using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using FamilyTree.Domain;

namespace FamilyTree.App.ViewModels;

/// <summary>Тип вводу дати в редакторі (T-5.2a). «Exact» — повна дата через DatePicker;
/// «Partial» — рік (+опц. місяць). Обидва мапляться на доменний <see cref="FamilyDateKind.Exact"/>,
/// але дають різний UI-ввід.</summary>
public enum FamilyDateInputKind
{
    Exact,
    Partial,
    Approximate,
    Range,
    Phrase,
}

/// <summary>Пункт випадайки: значення + ключ ресурсу для локалізованого підпису.</summary>
public sealed record FamilyDateOption<T>(T Value, string ResourceKey);

/// <summary>
/// Логіка контрола вибору неточної дати (T-5.2a, Частина 2): двобічне перетворення між
/// плоскими полями вводу й доменним <see cref="FamilyDate"/>. WPF-незалежна (лише
/// CommunityToolkit.Mvvm) — уся видима частина буде в XAML `FamilyDateEditor`, який
/// слухає <see cref="ValueChanged"/> і викликає <see cref="BuildValue"/>/<see cref="LoadValue"/>.
/// День задається лише через «Exact» (DatePicker); часткова/приблизна/діапазон — рік (+місяць).
/// </summary>
public partial class FamilyDateEditorViewModel : ObservableObject
{
    // Поки true (під час LoadValue) зміни полів не піднімають ValueChanged — інакше було б
    // відлуння DP → поля → ValueChanged → DP.
    private bool _loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExact), nameof(ShowPartial), nameof(ShowApproximate),
        nameof(ShowRange), nameof(ShowPhrase), nameof(ShowPoint), nameof(ShowSecondPoint), nameof(ShowCalendar))]
    private FamilyDateInputKind _selectedKind = FamilyDateInputKind.Exact;

    [ObservableProperty]
    private DateTime? _exactDate;

    [ObservableProperty]
    private int? _year;

    [ObservableProperty]
    private int? _month;

    [ObservableProperty]
    private DateApproximation _approximation = DateApproximation.About;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSecondPoint))]
    private DateRangeKind _rangeKind = DateRangeKind.Before;

    [ObservableProperty]
    private int? _year2;

    [ObservableProperty]
    private int? _month2;

    [ObservableProperty]
    private bool _isJulian;

    [ObservableProperty]
    private string? _phraseText;

    /// <summary>Спрацьовує, коли ввід змінився (окрім періоду <see cref="LoadValue"/>).</summary>
    public event EventHandler? ValueChanged;

    // ---- Списки для випадайок (значення + ключ ресурсу) ------------------

    public IReadOnlyList<FamilyDateOption<FamilyDateInputKind>> Kinds { get; } =
    [
        new(FamilyDateInputKind.Exact, "DateType_Exact"),
        new(FamilyDateInputKind.Partial, "DateType_Partial"),
        new(FamilyDateInputKind.Approximate, "DateType_Approximate"),
        new(FamilyDateInputKind.Range, "DateType_Range"),
        new(FamilyDateInputKind.Phrase, "DateType_Phrase"),
    ];

    public IReadOnlyList<FamilyDateOption<DateApproximation>> Approximations { get; } =
    [
        new(DateApproximation.About, "DateApprox_About"),
        new(DateApproximation.Calculated, "DateApprox_Calculated"),
        new(DateApproximation.Estimated, "DateApprox_Estimated"),
    ];

    public IReadOnlyList<FamilyDateOption<DateRangeKind>> Ranges { get; } =
    [
        new(DateRangeKind.Before, "DateRange_Before"),
        new(DateRangeKind.After, "DateRange_After"),
        new(DateRangeKind.Between, "DateRange_Between"),
    ];

    /// <summary>Номери місяців 1..12 для комбобокса (порожній вибір = «місяць не вказано»).</summary>
    public IReadOnlyList<int> MonthNumbers { get; } = Enumerable.Range(1, 12).ToList();

    // ---- Видимість блоків вводу за типом ---------------------------------

    public bool ShowExact => SelectedKind == FamilyDateInputKind.Exact;
    public bool ShowPartial => SelectedKind == FamilyDateInputKind.Partial;
    public bool ShowApproximate => SelectedKind == FamilyDateInputKind.Approximate;
    public bool ShowRange => SelectedKind == FamilyDateInputKind.Range;
    public bool ShowPhrase => SelectedKind == FamilyDateInputKind.Phrase;

    /// <summary>Показувати поля рік/місяць (часткова, приблизна, діапазон).</summary>
    public bool ShowPoint => SelectedKind is FamilyDateInputKind.Partial
        or FamilyDateInputKind.Approximate or FamilyDateInputKind.Range;

    /// <summary>Показувати другу точку (лише діапазон «між … і»).</summary>
    public bool ShowSecondPoint => ShowRange && RangeKind == DateRangeKind.Between;

    /// <summary>Перемикач календаря доречний для типів із датою-точкою (не Exact/Phrase).</summary>
    public bool ShowCalendar => ShowPoint;

    // ---- Перетворення ----------------------------------------------------

    /// <summary>Збирає <see cref="FamilyDate"/> з поточних полів; null, якщо обов'язкове порожнє.</summary>
    public FamilyDate? BuildValue() => SelectedKind switch
    {
        FamilyDateInputKind.Exact =>
            ExactDate is { } dt ? FamilyDate.Exact(DateOnly.FromDateTime(dt)) : null,

        FamilyDateInputKind.Partial =>
            MakePoint(Year, Month) is { } p ? FamilyDate.Exact(p) : null,

        FamilyDateInputKind.Approximate =>
            MakePoint(Year, Month) is { } p ? FamilyDate.Approximate(Approximation, p) : null,

        FamilyDateInputKind.Range => BuildRange(),

        FamilyDateInputKind.Phrase =>
            string.IsNullOrWhiteSpace(PhraseText) ? null : FamilyDate.FromPhrase(PhraseText),

        _ => null,
    };

    /// <summary>Заповнює поля з <see cref="FamilyDate"/> (виклик із контрола при зміні DP Value).</summary>
    public void LoadValue(FamilyDate? date)
    {
        _loading = true;
        try
        {
            ExactDate = null;
            Year = null;
            Month = null;
            Year2 = null;
            Month2 = null;
            Approximation = DateApproximation.About;
            RangeKind = DateRangeKind.Before;
            IsJulian = false;
            PhraseText = null;

            switch (date?.Kind)
            {
                case FamilyDateKind.Exact when date.Start is { } p:
                    if (p.Precision == DatePrecision.Day)
                    {
                        SelectedKind = FamilyDateInputKind.Exact;
                        ExactDate = p.ToDateOnly()?.ToDateTime(TimeOnly.MinValue);
                    }
                    else
                    {
                        SelectedKind = FamilyDateInputKind.Partial;
                        LoadPoint(p);
                    }

                    break;

                case FamilyDateKind.Approximate when date.Start is { } p:
                    SelectedKind = FamilyDateInputKind.Approximate;
                    Approximation = date.Approximation ?? DateApproximation.About;
                    LoadPoint(p);
                    break;

                case FamilyDateKind.Range when date.Start is { } p:
                    SelectedKind = FamilyDateInputKind.Range;
                    RangeKind = date.RangeKind ?? DateRangeKind.Before;
                    LoadPoint(p);
                    if (date.End is { } end)
                    {
                        Year2 = end.Year;
                        Month2 = end.Month;
                    }

                    break;

                case FamilyDateKind.Phrase:
                    SelectedKind = FamilyDateInputKind.Phrase;
                    PhraseText = date.Phrase;
                    break;

                default:
                    SelectedKind = FamilyDateInputKind.Exact;
                    break;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private FamilyDate? BuildRange()
    {
        if (MakePoint(Year, Month) is not { } from)
        {
            return null;
        }

        return RangeKind switch
        {
            DateRangeKind.Before => FamilyDate.Before(from),
            DateRangeKind.After => FamilyDate.After(from),
            DateRangeKind.Between => MakePoint(Year2, Month2) is { } to ? FamilyDate.Between(from, to) : null,
            _ => null,
        };
    }

    private DatePoint? MakePoint(int? year, int? month) => year is { } y
        ? new DatePoint
        {
            Year = y,
            Month = month,
            Calendar = IsJulian ? DateCalendar.Julian : DateCalendar.Gregorian,
        }
        : null;

    private void LoadPoint(DatePoint point)
    {
        Year = point.Year;
        Month = point.Month;
        IsJulian = point.Calendar == DateCalendar.Julian;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Не реагуємо під час завантаження й на суто похідні прапорці видимості (Show*).
        if (_loading || e.PropertyName is null || e.PropertyName.StartsWith("Show", StringComparison.Ordinal))
        {
            return;
        }

        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
