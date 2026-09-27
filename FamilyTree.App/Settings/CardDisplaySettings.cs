namespace FamilyTree.App.Settings;

/// <summary>
/// Спільна частина всіх трьох карток: два кеглі й розмір фото. Саме ДВА кеглі, а не
/// десяток окремих чисел і не один множник на все — проміжний варіант із розбору в
/// `IDEAS.md`, який на картці вузла виявився робочим: «головне» (імена) й «другорядне»
/// (підписи, дати, роки) — це рівно те, що людина хоче розводити за розміром.
///
/// Розміри самих карток тут немає: підказки міряє WPF (це <c>Border</c> із <c>Padding</c>),
/// а вузол рахує <c>NodeCardMetrics</c> із цих же чисел.
/// </summary>
public abstract class CardSizeSettings
{
    /// <summary>Найменший кегль: дрібніше вже нечитно на будь-якому екрані.</summary>
    public const double MinFontSize = 8;

    /// <summary>Найбільший кегль.</summary>
    public const double MaxFontSize = 32;

    public const double MinPhotoHeight = 24;

    public const double MaxPhotoHeight = 200;

    /// <summary>
    /// Ширина фото відносно висоти. Спільна для всіх трьох карток: обличчя в різних
    /// пропорціях у сусідніх місцях виглядало б недбало.
    /// Число — це рівно 92/112, тобто пропорція, яка була зашита в картці-підказці;
    /// так типові налаштування відтворюють колишні 92×112 і 64×78 точно. Мініатюра
    /// вузла від цього стає 41×50 замість 40×50 — один піксель, і лише коли фото
    /// увімкнене (типово воно вимкнене).
    /// </summary>
    public const double PhotoAspect = 92.0 / 112.0;

    /// <summary>
    /// Виставляє типові розміри цієї картки. Виклик абстрактних членів із конструктора
    /// тут безпечний навмисно: всі три перевизначення — константи, жодного поля нащадка
    /// вони не читають. Без цього щойно створений об'єкт мав би нулі, і картка стала б
    /// невидимою ще до того, як налаштування завантажаться з файлу.
    /// </summary>
    protected CardSizeSettings() => ResetSizes();

    /// <summary>Кегль головних рядків: імена.</summary>
    public double PrimaryFontSize { get; set; }

    /// <summary>Кегль другорядних: підписи, дати, роки, бейдж родства.</summary>
    public double SecondaryFontSize { get; set; }

    /// <summary>Висота фото; ширина — похідна від неї.</summary>
    public double PhotoHeight { get; set; }

    /// <summary>Ширина фото, похідна від висоти.</summary>
    public double PhotoWidth => PhotoHeight * PhotoAspect;

    // Значення, зведені до меж, без зміни самого об'єкта. Потрібні тим, хто рахує розмір
    // (NodeCardMetrics) і будує картки: ClampSizes() відпрацьовує при завантаженні
    // налаштувань, але об'єкт можуть створити й поза цим шляхом.
    public double SafePrimaryFontSize =>
        Clamp(PrimaryFontSize, MinFontSize, MaxFontSize, DefaultPrimaryFontSize);

    public double SafeSecondaryFontSize =>
        Clamp(SecondaryFontSize, MinFontSize, MaxFontSize, DefaultSecondaryFontSize);

    public double SafePhotoHeight =>
        Clamp(PhotoHeight, MinPhotoHeight, MaxPhotoHeight, DefaultPhotoHeight);

    /// <summary>Ширина фото за зведеною висотою.</summary>
    public double SafePhotoWidth => SafePhotoHeight * PhotoAspect;

    /// <summary>
    /// Ширина, в якій декодувати мініатюру: удвічі більша за показану (запас на 200% DPI),
    /// але не менша за <paramref name="baseline"/> — типове значення цієї картки.
    /// Без цього фото замилювалося б, щойно повзунок переходить за типовий розмір:
    /// декодована ширина була константою, а показана — більше ні.
    /// </summary>
    public int PhotoDecodeWidth(int baseline) =>
        (int)Math.Max(baseline, Math.Ceiling(SafePhotoWidth * 2));

    /// <summary>Типовий кегль головних рядків цієї картки.</summary>
    protected abstract double DefaultPrimaryFontSize { get; }

    /// <summary>Типовий кегль другорядних рядків цієї картки.</summary>
    protected abstract double DefaultSecondaryFontSize { get; }

    /// <summary>Типова висота фото цієї картки.</summary>
    protected abstract double DefaultPhotoHeight { get; }

    /// <summary>
    /// Зводить розміри до допустимих. Живе тут, поруч із межами: settings.json правиться
    /// руками, і 0, від'ємне чи NaN дали б невидимий текст або картку нульового розміру
    /// ще до першого вікна.
    /// </summary>
    public void ClampSizes()
    {
        PrimaryFontSize = Clamp(PrimaryFontSize, MinFontSize, MaxFontSize, DefaultPrimaryFontSize);
        SecondaryFontSize = Clamp(SecondaryFontSize, MinFontSize, MaxFontSize, DefaultSecondaryFontSize);
        PhotoHeight = Clamp(PhotoHeight, MinPhotoHeight, MaxPhotoHeight, DefaultPhotoHeight);
    }

    /// <summary>Повертає розміри до типових для цієї картки.</summary>
    public void ResetSizes()
    {
        PrimaryFontSize = DefaultPrimaryFontSize;
        SecondaryFontSize = DefaultSecondaryFontSize;
        PhotoHeight = DefaultPhotoHeight;
    }

    /// <summary>Значення в межах, або типове — якщо воно нечисло, нуль чи від'ємне.</summary>
    public static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) && value > 0 ? Math.Clamp(value, min, max) : fallback;
}

/// <summary>
/// Зміст трьох карток застосунку: вузол дерева, підказка особи, підказка подружжя.
/// Саме ЗМІСТ, а не розміри: тут вирішується, які рядки писати й чи показувати фото,
/// а шрифти й геометрія лишаються в XAML і в <c>TreeLayoutEngine</c>.
///
/// Чому окремим класом, а не полями <see cref="AppSettings"/>: у settings.json це дає
/// три зрозумілі вкладені об'єкти замість двадцяти плоских прапорців, і кожна вкладка
/// вікна налаштувань відповідає рівно одному з них.
/// </summary>
public sealed class CardDisplaySettings
{
    /// <summary>Картка вузла на полотні дерева.</summary>
    public NodeCardSettings Node { get; set; } = new();

    /// <summary>Велика підказка особи (той самий шаблон у дереві й на вкладці «Особа»).</summary>
    public PersonTooltipSettings PersonTooltip { get; set; } = new();

    /// <summary>Підказка рамки подружжя.</summary>
    public CoupleTooltipSettings CoupleTooltip { get; set; } = new();

    /// <summary>
    /// Замінює відсутні групи типовими. Потрібно саме тут, а не в місці читання:
    /// settings.json правиться руками, і рядок <c>"node": null</c> інакше поклав би
    /// застосунок NullReferenceException-ом ще до появи головного вікна.
    /// </summary>
    public void Normalize()
    {
        Node ??= new NodeCardSettings();
        PersonTooltip ??= new PersonTooltipSettings();
        CoupleTooltip ??= new CoupleTooltipSettings();

        Node.ClampSizes();
        PersonTooltip.ClampSizes();
        CoupleTooltip.ClampSizes();
    }
}

/// <summary>
/// Що писати у вузлі дерева. Картка мала (160×80), тож увімкнення всього одразу
/// призведе до обрізання рядків — саме тому вікно налаштувань показує живий перегляд.
/// </summary>
public sealed class NodeCardSettings : CardSizeSettings
{
    /// <summary>Мініатюра фото ліворуч від тексту. Типово вимкнена: вона з'їдає третину ширини картки.</summary>
    public bool ShowPhoto { get; set; }

    /// <summary>Бейдж родства відносно кореня («батько», «двоюрідна сестра»).</summary>
    public bool ShowRelationBadge { get; set; } = true;

    /// <summary>По батькові окремим рядком.</summary>
    public bool ShowPatronymic { get; set; } = true;

    /// <summary>Дівоче прізвище окремим рядком.</summary>
    public bool ShowMaidenName { get; set; }

    /// <summary>Роки життя «1980–2021».</summary>
    public bool ShowYears { get; set; } = true;

    /// <summary>Порядок імені: <c>true</c> — «Прізвище Ім'я», <c>false</c> — «Ім'я Прізвище».</summary>
    public bool SurnameFirst { get; set; } = true;

    protected override double DefaultPrimaryFontSize => 13;

    protected override double DefaultSecondaryFontSize => 11;

    protected override double DefaultPhotoHeight => 50;

    public void Reset()
    {
        ShowPhoto = false;
        ShowRelationBadge = true;
        ShowPatronymic = true;
        ShowMaidenName = false;
        ShowYears = true;
        SurnameFirst = true;
        ResetSizes();
    }
}

/// <summary>
/// Рядки великої підказки особи. Ім'я не налаштовується: підказка без імені
/// не має сенсу, і вимкнути його — єдиний спосіб зробити її беззмістовною.
/// </summary>
public sealed class PersonTooltipSettings : CardSizeSettings
{
    // Типові — ті самі числа, що були зашиті в PersonCardTemplate: ім'я 15, рядки 12,
    // фото 112 заввишки (92 завширшки — з тієї ж пропорції 4:5).
    protected override double DefaultPrimaryFontSize => 15;

    protected override double DefaultSecondaryFontSize => 12;

    protected override double DefaultPhotoHeight => 112;

    public bool ShowPhoto { get; set; } = true;

    public bool ShowRelationBadge { get; set; } = true;

    public bool ShowMaidenName { get; set; } = true;

    public bool ShowGender { get; set; } = true;

    public bool ShowBirth { get; set; } = true;

    public bool ShowDeath { get; set; } = true;

    public bool ShowMarriages { get; set; } = true;

    public bool ShowChildrenCount { get; set; } = true;

    public bool ShowFacts { get; set; } = true;

    public bool ShowNotes { get; set; } = true;

    public void Reset()
    {
        ShowPhoto = true;
        ShowRelationBadge = true;
        ShowMaidenName = true;
        ShowGender = true;
        ShowBirth = true;
        ShowDeath = true;
        ShowMarriages = true;
        ShowChildrenCount = true;
        ShowFacts = true;
        ShowNotes = true;
        ResetSizes();
    }
}

/// <summary>
/// Рядки підказки рамки подружжя. Імена подружжя показуються завжди — без них
/// підказка не пояснює, про яку пару йдеться.
/// </summary>
public sealed class CoupleTooltipSettings : CardSizeSettings
{
    // Типові — з CoupleCardTemplate: імена 13, роки й рядки 12, фото 78 заввишки.
    // Роки раніше були 11, тепер ідуть тим самим другорядним кеглем, що й рядки
    // шлюбу: два майже однакові розміри поруч нічого не додавали.
    protected override double DefaultPrimaryFontSize => 13;

    protected override double DefaultSecondaryFontSize => 12;

    protected override double DefaultPhotoHeight => 78;

    /// <summary>Два фото поруч над іменами.</summary>
    public bool ShowPhotos { get; set; } = true;

    /// <summary>Роки життя під кожним іменем.</summary>
    public bool ShowYears { get; set; } = true;

    /// <summary>Дата шлюбу.</summary>
    public bool ShowMarriageDate { get; set; } = true;

    /// <summary>Місце шлюбу (GEDCOM <c>MARR.PLAC</c>).</summary>
    public bool ShowMarriagePlace { get; set; } = true;

    /// <summary>Тривалість шлюбу в роках. Рахується лише коли відома дата шлюбу.</summary>
    public bool ShowDuration { get; set; }

    /// <summary>Кількість спільних дітей пари.</summary>
    public bool ShowChildrenCount { get; set; } = true;

    public void Reset()
    {
        ShowPhotos = true;
        ShowYears = true;
        ShowMarriageDate = true;
        ShowMarriagePlace = true;
        ShowDuration = false;
        ShowChildrenCount = true;
        ResetSizes();
    }
}
