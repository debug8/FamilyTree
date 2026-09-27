using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyTree.App.Localization;
using FamilyTree.App.Settings;
using FamilyTree.Domain;
using FamilyTree.Domain.Layout;
using FamilyTree.Storage;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Вікно «Налаштування карток»: три вкладки — вузол дерева, підказка особи,
/// підказка подружжя. Налаштовується ЗМІСТ (які рядки писати, чи показувати фото),
/// а не розміри: шрифти й геометрія лишаються в XAML і в TreeLayoutEngine.
///
/// Кожна галочка одразу пише в settings.json і просить власника перебудувати картки —
/// той самий підхід, що й у <see cref="SettingsViewModel"/>, без кнопки «Застосувати».
/// </summary>
public partial class CardSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _localization;
    private readonly PersonCardBuilder _cards;
    private readonly CoupleCardBuilder _coupleCards;

    /// <summary>
    /// Перебудувати картки застосунку. Передається ззовні (MainViewModel), а не через
    /// посилання на TreeViewModel: перемальовувати треба і дерево, і вкладку «Особа»,
    /// і діалог не має знати, як саме це робиться.
    /// </summary>
    private readonly Action _applyChanges;

    // Скидання виставляє обидва повзунки підряд. Без цього прапорця кожне присвоєння
    // смикнуло б власний partial-обробник і перебудувало дерево — тричі на одне скидання.
    private bool _suspendApply;

    // Демо-родина для перегляду. Будується раз у конструкторі: перегляд має показувати
    // картку зі ВСІМА заповненими полями, інакше половина галочок нічого не міняла б
    // візуально й виглядала б зламаною.
    private readonly FamilyDocument _demoDoc;
    private readonly Dictionary<Guid, Person> _demoPersons;
    private readonly Person _demoPerson;
    private readonly Person _demoSpouse;
    private readonly SpouseLink _demoLink;

    // Фото й два шрифти — окремими властивостями ViewModel, на відміну від галочок, які
    // прив'язуються прямо в об'єкт налаштувань. Причина в тому, що повзунок не має Command:
    // про його рух треба дізнатися якось інакше, і partial-метод згенерованої властивості —
    // найпростіший спосіб. Розміру САМОЇ картки серед них немає: він рахується з цих трьох
    // чисел і набору галочок (NodeCardMetrics).
    [ObservableProperty]
    private double _photoHeight;

    [ObservableProperty]
    private double _primaryFontSize;

    [ObservableProperty]
    private double _secondaryFontSize;

    [ObservableProperty]
    private double _personPhotoHeight;

    [ObservableProperty]
    private double _personPrimaryFontSize;

    [ObservableProperty]
    private double _personSecondaryFontSize;

    [ObservableProperty]
    private double _couplePhotoHeight;

    [ObservableProperty]
    private double _couplePrimaryFontSize;

    [ObservableProperty]
    private double _coupleSecondaryFontSize;

    public CardSettingsViewModel(
        ISettingsService settings,
        ILocalizationService localization,
        Action applyChanges)
    {
        _settings = settings;
        _localization = localization;
        _applyChanges = applyChanges;
        _cards = new PersonCardBuilder(localization, settings);
        _coupleCards = new CoupleCardBuilder(localization, settings);

        // Clamp уже зроблено при завантаженні налаштувань (CardDisplaySettings.Normalize),
        // тож тут просто читаємо — повзунок не опиниться поза своєю шкалою.
        var node = _settings.Current.Cards.Node;
        _photoHeight = node.PhotoHeight;
        _primaryFontSize = node.PrimaryFontSize;
        _secondaryFontSize = node.SecondaryFontSize;

        var person = _settings.Current.Cards.PersonTooltip;
        _personPhotoHeight = person.PhotoHeight;
        _personPrimaryFontSize = person.PrimaryFontSize;
        _personSecondaryFontSize = person.SecondaryFontSize;

        var couple = _settings.Current.Cards.CoupleTooltip;
        _couplePhotoHeight = couple.PhotoHeight;
        _couplePrimaryFontSize = couple.PrimaryFontSize;
        _coupleSecondaryFontSize = couple.SecondaryFontSize;

        (_demoDoc, _demoPersons, _demoPerson, _demoSpouse, _demoLink) = BuildDemoFamily();

        _localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Налаштування вузла дерева — прив'язуються прямо з XAML.</summary>
    public NodeCardSettings Node => _settings.Current.Cards.Node;

    /// <summary>Налаштування підказки особи.</summary>
    public PersonTooltipSettings PersonTooltip => _settings.Current.Cards.PersonTooltip;

    /// <summary>Налаштування підказки подружжя.</summary>
    public CoupleTooltipSettings CoupleTooltip => _settings.Current.Cards.CoupleTooltip;

    // Межі спільні для всіх трьох карток — див. CardSizeSettings.
    public static double MinPhotoHeight => CardSizeSettings.MinPhotoHeight;

    public static double MaxPhotoHeight => CardSizeSettings.MaxPhotoHeight;

    public static double MinFontSize => CardSizeSettings.MinFontSize;

    public static double MaxFontSize => CardSizeSettings.MaxFontSize;

    /// <summary>Перегляд картки вузла на демо-особі.</summary>
    public TreeNodeViewModel NodePreview => BuildNodePreview();

    /// <summary>Перегляд великої підказки особи.</summary>
    public PersonCard PersonPreview => BuildPersonPreview();

    /// <summary>Перегляд підказки подружжя.</summary>
    public CoupleCard CouplePreview => BuildCouplePreview();

    /// <summary>
    /// Викликається з XAML після кожної галочки. Прив'язки йдуть напряму в об'єкти
    /// налаштувань (вони не ObservableObject), тож факт зміни до ViewModel інакше б не дійшов —
    /// звідси команда, а не <c>partial void On…Changed</c>.
    /// </summary>
    [RelayCommand]
    private void OptionChanged() => ApplyAndRefresh();

    // Повзунки. Прив'язка в XAML із Delay, тож сюди приходить не кожен піксель
    // перетягування, а значення після короткої паузи — інакше кожен тік перебудовував би
    // усе дерево, а на великій родині це сотні вузлів із перерахунком координат.
    partial void OnPhotoHeightChanged(double value)
    {
        _settings.Current.Cards.Node.PhotoHeight = value;
        ApplyAndRefresh();
    }

    partial void OnPrimaryFontSizeChanged(double value)
    {
        _settings.Current.Cards.Node.PrimaryFontSize = value;
        ApplyAndRefresh();
    }

    partial void OnSecondaryFontSizeChanged(double value)
    {
        _settings.Current.Cards.Node.SecondaryFontSize = value;
        ApplyAndRefresh();
    }

    partial void OnPersonPhotoHeightChanged(double value)
    {
        _settings.Current.Cards.PersonTooltip.PhotoHeight = value;
        ApplyAndRefresh();
    }

    partial void OnPersonPrimaryFontSizeChanged(double value)
    {
        _settings.Current.Cards.PersonTooltip.PrimaryFontSize = value;
        ApplyAndRefresh();
    }

    partial void OnPersonSecondaryFontSizeChanged(double value)
    {
        _settings.Current.Cards.PersonTooltip.SecondaryFontSize = value;
        ApplyAndRefresh();
    }

    partial void OnCouplePhotoHeightChanged(double value)
    {
        _settings.Current.Cards.CoupleTooltip.PhotoHeight = value;
        ApplyAndRefresh();
    }

    partial void OnCouplePrimaryFontSizeChanged(double value)
    {
        _settings.Current.Cards.CoupleTooltip.PrimaryFontSize = value;
        ApplyAndRefresh();
    }

    partial void OnCoupleSecondaryFontSizeChanged(double value)
    {
        _settings.Current.Cards.CoupleTooltip.SecondaryFontSize = value;
        ApplyAndRefresh();
    }

    [RelayCommand]
    private void ResetNode()
    {
        Node.Reset();

        _suspendApply = true;
        PhotoHeight = Node.PhotoHeight;
        PrimaryFontSize = Node.PrimaryFontSize;
        SecondaryFontSize = Node.SecondaryFontSize;
        _suspendApply = false;

        // Порожнє ім'я = «усі властивості»: об'єкти налаштувань не сповіщають про зміни самі,
        // тож після скидання галочки перечитають свої значення лише за таким загальним поштовхом.
        OnPropertyChanged(string.Empty);
        ApplyAndRefresh();
    }

    [RelayCommand]
    private void ResetPersonTooltip()
    {
        PersonTooltip.Reset();

        _suspendApply = true;
        PersonPhotoHeight = PersonTooltip.PhotoHeight;
        PersonPrimaryFontSize = PersonTooltip.PrimaryFontSize;
        PersonSecondaryFontSize = PersonTooltip.SecondaryFontSize;
        _suspendApply = false;

        // Порожнє ім'я = «усі властивості»: об'єкти налаштувань не сповіщають про зміни самі,
        // тож після скидання галочки перечитають свої значення лише за таким загальним поштовхом.
        OnPropertyChanged(string.Empty);
        ApplyAndRefresh();
    }

    [RelayCommand]
    private void ResetCoupleTooltip()
    {
        CoupleTooltip.Reset();

        _suspendApply = true;
        CouplePhotoHeight = CoupleTooltip.PhotoHeight;
        CouplePrimaryFontSize = CoupleTooltip.PrimaryFontSize;
        CoupleSecondaryFontSize = CoupleTooltip.SecondaryFontSize;
        _suspendApply = false;

        // Порожнє ім'я = «усі властивості»: об'єкти налаштувань не сповіщають про зміни самі,
        // тож після скидання галочки перечитають свої значення лише за таким загальним поштовхом.
        OnPropertyChanged(string.Empty);
        ApplyAndRefresh();
    }

    /// <summary>Викликати при закритті діалогу — відписатися від подій.</summary>
    public void Detach() => _localization.LanguageChanged -= OnLanguageChanged;

    private void ApplyAndRefresh()
    {
        if (_suspendApply)
        {
            return;
        }

        _settings.Save();
        RefreshPreviews();
        _applyChanges();
    }

    private void RefreshPreviews()
    {
        OnPropertyChanged(nameof(NodePreview));
        OnPropertyChanged(nameof(PersonPreview));
        OnPropertyChanged(nameof(CouplePreview));
    }

    // Мова змінилася в іншому вікні — підписи всередині карток перекладаються,
    // тож перегляди треба зібрати наново.
    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshPreviews();

    private TreeNodeViewModel BuildNodePreview()
    {
        var o = Node;
        var size = NodeCardMetrics.Measure(o);

        return new TreeNodeViewModel(_demoPerson.Id)
        {
            NamePrimary = PersonCardBuilder.FormatNamePrimary(_demoPerson, o.SurnameFirst),
            Patronymic = o.ShowPatronymic ? PersonCardBuilder.FormatPatronymic(_demoPerson) : null,
            MaidenName = o.ShowMaidenName ? _demoPerson.MaidenName : null,
            Years = o.ShowYears ? PersonCardBuilder.FormatYears(_demoPerson) : string.Empty,
            RelationBadge = o.ShowRelationBadge ? _localization.GetString("CardSettings_DemoBadge") : null,
            // Той самий розрахунок, що й у дереві — перегляд показує справжній розмір.
            Width = TreeLayoutEngine.ClampNodeWidth(size.Width),
            Height = TreeLayoutEngine.ClampNodeHeight(size.Height),
            PrimaryFontSize = o.PrimaryFontSize,
            SecondaryFontSize = o.SecondaryFontSize,
            PhotoWidth = o.PhotoWidth,
            PhotoHeight = o.PhotoHeight,
            ShowPhoto = o.ShowPhoto,

            // Фото в перегляді немає навіть коли воно ввімкнене: демо-особа вигадана, і
            // підставляти їй чиєсь обличчя ні з чого. Порожня рамка чесно показує головне —
            // скільки місця мініатюра забере в тексту.
            Photo = null,
        };
    }

    private PersonCard BuildPersonPreview() =>
        _cards.Build(
            _demoPerson,
            _demoDoc,
            _demoPersons,
            childrenCount: 2,
            relationBadge: _localization.GetString("CardSettings_DemoBadge"));

    private CoupleCard BuildCouplePreview() =>
        _coupleCards.Build(_demoPerson, _demoSpouse, _demoLink, childrenCount: 2);

    /// <summary>
    /// Вигадана пара з усіма заповненими полями — щоб кожна галочка мала що показати
    /// чи сховати. Імена беруться з ресурсів, тож перегляд говорить мовою інтерфейсу.
    /// </summary>
    private (FamilyDocument Doc, Dictionary<Guid, Person> Persons, Person A, Person B, SpouseLink Link)
        BuildDemoFamily()
    {
        var she = new Person
        {
            LastName = _localization.GetString("CardSettings_DemoLastName"),
            FirstName = _localization.GetString("CardSettings_DemoFirstName"),
            MiddleName = _localization.GetString("CardSettings_DemoMiddleName"),
            MaidenName = _localization.GetString("CardSettings_DemoMaidenName"),
            Gender = Gender.Female,
            Birth = PersonEvent.Create(
                new DateOnly(1952, 4, 17), _localization.GetString("CardSettings_DemoBirthPlace")),
            Death = PersonEvent.Create(
                new DateOnly(2019, 11, 3), _localization.GetString("CardSettings_DemoDeathPlace")),
            Notes = _localization.GetString("CardSettings_DemoNotes"),
        };
        she.Facts.Add(new PersonFact
        {
            Kind = PersonFactKind.Occupation,
            Value = _localization.GetString("CardSettings_DemoOccupation"),
        });

        var he = new Person
        {
            LastName = _localization.GetString("CardSettings_DemoSpouseLastName"),
            FirstName = _localization.GetString("CardSettings_DemoSpouseFirstName"),
            Gender = Gender.Male,
            Birth = PersonEvent.Create(new DateOnly(1949, 8, 2)),
            Death = PersonEvent.Create(new DateOnly(2021, 1, 20)),
        };

        var link = SpouseLink.Create(she.Id, he.Id, new DateOnly(1974, 9, 28));
        link.MarriagePlace = _localization.GetString("CardSettings_DemoMarriagePlace");

        var doc = FamilyDocument.CreateNew("preview");
        doc.Persons.Add(she);
        doc.Persons.Add(he);
        doc.SpouseLinks.Add(link);

        return (doc, new Dictionary<Guid, Person> { [she.Id] = she, [he.Id] = he }, she, he, link);
    }
}
