using System.Linq;

namespace FamilyTree.Domain.Kinship;

/// <summary>
/// T-3.2 — українські назви родства (розд. 4.3).
/// Бічні лінії (a, b ≥ 1), k = min(a, b), d = |a − b|:
///  • d = 0 → «k-юрідний» брат/сестра (k = 1 — рідні, з поділом єдинокровні/єдиноутробні);
///  • b &lt; a (старша гілка): d = 1 → дядько/тітка з префіксом порядку k;
///    d ≥ 2 → дід/баба (d = 2) чи пра…дід (d ≥ 3) з префіксом порядку k + 1
///    (брат діда — «двоюрідний дід», двоюрідний брат діда — «троюрідний дід»);
///  • b &gt; a (молодша гілка): d = 1 → племінник; d = 2 → внучатий племінник;
///    d ≥ 3 → пра…внучатий племінник; префікс порядку k (для k ≥ 2).
/// </summary>
public sealed class UkrainianKinshipFormatter : IKinshipFormatter
{
    private static readonly string[] OrdinalStems =
    {
        "", "", "двоюрідн", "троюрідн", "чотириюрідн", "п'ятиюрідн", "шестиюрідн", "семиюрідн",
    };

    public string CultureCode => "uk";

    public KinshipNamingStyle Style { get; set; } = KinshipNamingStyle.Standard;

    public string Format(in KinshipContext context)
    {
        var c = context; // локальна копія: in-параметр не можна захоплювати в лямбду (CS1628)
        var name = c.Kind switch
        {
            KinshipKind.SamePerson => "та сама особа",
            KinshipKind.None => "родинний зв'язок не встановлено",
            // Подружжя та свояцтво теж проходять через ByGender: раніше вони кликали Pick
            // напряму, і особа з Gender.Unknown тихо ставала чоловіком («чоловік», «зять»).
            KinshipKind.Spouse => c.IsFormerSpouse
                ? ByGender(c.RelativeGender, () => "колишній чоловік", () => "колишня дружина")
                : ByGender(c.RelativeGender, () => "чоловік", () => "дружина"),
            KinshipKind.Affinity => ByGender(
                c.RelativeGender,
                () => BuildAffinity(c, Gender.Male, Style == KinshipNamingStyle.Detailed),
                () => BuildAffinity(c, Gender.Female, Style == KinshipNamingStyle.Detailed)),
            _ => ByGender(c.RelativeGender, () => Build(c, Gender.Male), () => Build(c, Gender.Female)),
        };

        return Style == KinshipNamingStyle.Detailed ? WithLineage(name, c) : name;
    }

    /// <summary>Додає уточнення лінії до бічних зв'язків старшої гілки та кузенів (a ≥ 2).</summary>
    private static string WithLineage(string name, KinshipContext c)
    {
        if (c.Kind != KinshipKind.Collateral || c.StepsUp < 2)
        {
            return name;
        }

        return c.Lineage switch
        {
            Lineage.Paternal => name + " (по батькові)",
            Lineage.Maternal => name + " (по матері)",
            _ => name,
        };
    }

    /// <summary>
    /// Для невідомої статі показуємо обидва варіанти: «син / дочка».
    /// Якщо назва для обох статей однакова — не дублюємо її.
    /// </summary>
    private static string ByGender(Gender gender, Func<string> male, Func<string> female)
    {
        switch (gender)
        {
            case Gender.Male:
                return male();
            case Gender.Female:
                return female();
            default:
                var m = male();
                var f = female();
                return m == f ? m : $"{m} / {f}";
        }
    }

    private static string Build(KinshipContext c, Gender g) => c.Kind switch
    {
        KinshipKind.DirectAncestor => c.StepsUp switch
        {
            1 => Pick(g, "батько", "мати"),
            2 => Pick(g, "дід", "баба"),
            _ => Pra(c.StepsUp - 2) + Pick(g, "дід", "баба"),
        },
        KinshipKind.DirectDescendant => c.StepsDown switch
        {
            1 => Pick(g, "син", "дочка"),
            2 => Pick(g, "онук", "онука"),
            _ => Pra(c.StepsDown - 2) + Pick(g, "внук", "внучка"),
        },
        KinshipKind.Collateral => BuildCollateral(c.StepsUp, c.StepsDown, g, c.SiblingKind),
        _ => string.Empty,
    };

    private static string BuildCollateral(int a, int b, Gender g, SiblingKind siblingKind)
    {
        var k = Math.Min(a, b);
        var d = Math.Abs(a - b);

        if (d == 0)
        {
            if (k == 1)
            {
                var word = Pick(g, "брат", "сестра");
                return siblingKind switch
                {
                    SiblingKind.HalfPaternal => Pick(g, "єдинокровний ", "єдинокровна ") + word,
                    SiblingKind.HalfMaternal => Pick(g, "єдиноутробний ", "єдиноутробна ") + word,
                    // «зведений» українською — це дитина мачухи/вітчима, тобто БЕЗ спільної
                    // крові (тепер AffinityKind.StepParent/StepChild). Для спільного одного
                    // з батьків правильний термін — «неповнорідний».
                    SiblingKind.HalfUnknown => Pick(g, "неповнорідний ", "неповнорідна ") + word,
                    _ => word,
                };
            }

            return $"{Ordinal(k, g)} {Pick(g, "брат", "сестра")}";
        }

        if (b < a) // старша гілка: дядьки, двоюрідні діди…
        {
            if (d == 1)
            {
                var word = Pick(g, "дядько", "тітка");
                return k == 1 ? word : $"{Ordinal(k, g)} {word}";
            }

            var baseWord = d == 2
                ? Pick(g, "дід", "баба")
                : Pra(d - 2) + Pick(g, "дід", "баба");
            return $"{Ordinal(k + 1, g)} {baseWord}";
        }

        // b > a — молодша гілка: племінники та їхні нащадки.
        var nephewWord = d switch
        {
            1 => Pick(g, "племінник", "племінниця"),
            2 => Pick(g, "внучатий племінник", "внучата племінниця"),
            _ => Pra(d - 2) + Pick(g, "внучатий племінник", "внучата племінниця"),
        };
        return k == 1 ? nephewWord : $"{Ordinal(k, g)} {nephewWord}";
    }

    /// <summary>
    /// Свояцтво (розд. 4.5): g — стать особи-B (передається явно, щоб для невідомої статі
    /// ByGender міг показати обидва варіанти), pivot — стать сполучної особи X.
    /// </summary>
    private static string BuildAffinity(KinshipContext c, Gender g, bool detailed)
    {
        var pivot = c.PivotGender;
        return c.Affinity switch
        {
            // B — подружжя мого батька/матері: вітчим (чоловік матері) / мачуха (дружина батька).
            AffinityKind.StepParent => Pick(g, "вітчим", "мачуха"),
            // B — дитина мого подружжя від іншого шлюбу.
            AffinityKind.StepChild => Pick(g, "пасинок", "пасербиця"),
            // B — батько/мати мого подружжя. Подружжя-жінка → батьки дружини (тесть/теща);
            // подружжя-чоловік → батьки чоловіка (свекор/свекруха). Якщо стать подружжя
            // невідома, українська назва невизначена — даємо описову замість вгадування.
            AffinityKind.SpouseParent => pivot switch
            {
                Gender.Female => Pick(g, "тесть", "теща"),
                Gender.Male => Pick(g, "свекор", "свекруха"),
                _ => Pick(g, "батько подружжя", "мати подружжя"),
            },
            // B — брат/сестра мого подружжя. Подружжя-чоловік → дівер/зовиця;
            // подружжя-жінка → шурин/своячка.
            AffinityKind.SpouseSibling => pivot switch
            {
                Gender.Male => Pick(g, "дівер", "зовиця"),
                Gender.Female => Pick(g, "шурин", "своячка"),
                _ => Pick(g, "брат подружжя", "сестра подружжя"),
            },
            // B — подружжя моєї дитини: зять (чоловік дочки) / невістка (дружина сина).
            AffinityKind.ChildSpouse => Pick(g, "зять", "невістка"),
            // B — подружжя мого сиблінга. Чоловік сестри — «зять»; у детальному стилі —
            // «шваґер (чоловік сестри)». Дружина брата — «невістка».
            AffinityKind.SiblingSpouse => g == Gender.Male
                ? (detailed ? "шваґер (чоловік сестри)" : "зять")
                : "невістка",
            // B — подружжя дядька/тітки (описово).
            AffinityKind.UncleAuntSpouse => Pick(g, "чоловік тітки", "дружина дядька"),
            _ => "свояк / родичка через шлюб",
        };
    }

    private static string Pick(Gender g, string male, string female) =>
        g == Gender.Female ? female : male;

    /// <summary>«пра» × n: Pra(1) → «пра», Pra(2) → «прапра»…</summary>
    private static string Pra(int count) => string.Concat(Enumerable.Repeat("пра", count));

    /// <summary>«двоюрідний/двоюрідна», «троюрідний»… для k ≥ 8 — «8-юрідний».</summary>
    private static string Ordinal(int k, Gender g)
    {
        var stem = k < OrdinalStems.Length ? OrdinalStems[k] : $"{k}-юрідн";
        return stem + (g == Gender.Female ? "а" : "ий");
    }
}
