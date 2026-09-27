namespace FamilyTree.App.Settings;

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
    }
}

/// <summary>
/// Що писати у вузлі дерева. Картка мала (160×80), тож увімкнення всього одразу
/// призведе до обрізання рядків — саме тому вікно налаштувань показує живий перегляд.
/// </summary>
public sealed class NodeCardSettings
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

    public void Reset()
    {
        ShowPhoto = false;
        ShowRelationBadge = true;
        ShowPatronymic = true;
        ShowMaidenName = false;
        ShowYears = true;
        SurnameFirst = true;
    }
}

/// <summary>
/// Рядки великої підказки особи. Ім'я не налаштовується: підказка без імені
/// не має сенсу, і вимкнути його — єдиний спосіб зробити її беззмістовною.
/// </summary>
public sealed class PersonTooltipSettings
{
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
    }
}

/// <summary>
/// Рядки підказки рамки подружжя. Імена подружжя показуються завжди — без них
/// підказка не пояснює, про яку пару йдеться.
/// </summary>
public sealed class CoupleTooltipSettings
{
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
    }
}
