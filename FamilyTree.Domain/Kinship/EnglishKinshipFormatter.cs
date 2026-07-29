using System.Linq;

namespace FamilyTree.Domain.Kinship;

/// <summary>
/// T-3.5 — англійські назви родства (розд. 4.7). Той самий алгоритм (KinshipContext),
/// інша мовна генерація: grand/great-grand-, uncle/aunt, nephew/niece,
/// кузени «N-th cousin M times removed» (не мапляться 1:1 на українські).
/// </summary>
public sealed class EnglishKinshipFormatter : IKinshipFormatter
{
    private static readonly string[] Ordinals =
    {
        string.Empty, "first", "second", "third", "fourth", "fifth", "sixth", "seventh",
    };

    public string CultureCode => "en";

    public KinshipNamingStyle Style { get; set; } = KinshipNamingStyle.Standard;

    public string Format(in KinshipContext context)
    {
        var c = context;
        var detailed = Style == KinshipNamingStyle.Detailed;
        var name = c.Kind switch
        {
            KinshipKind.SamePerson => "the same person",
            KinshipKind.None => "no relation",
            // Подружжя та свояцтво теж проходять через ByGender: раніше вони кликали Pick
            // напряму, і особа з Gender.Unknown тихо ставала чоловіком («husband», «son-in-law»).
            KinshipKind.Spouse => WithAlsoBlood(
                c.IsFormerSpouse
                    ? ByGender(c.RelativeGender, () => "ex-husband", () => "ex-wife")
                    : ByGender(c.RelativeGender, () => "husband", () => "wife"),
                c.BloodRelationName),
            KinshipKind.Affinity => ByGender(
                c.RelativeGender,
                () => BuildAffinity(c, Gender.Male),
                () => BuildAffinity(c, Gender.Female)),
            _ => ByGender(c.RelativeGender, () => Build(c, Gender.Male, detailed), () => Build(c, Gender.Female, detailed)),
        };

        return detailed ? WithLineage(name, c) : name;
    }

    /// <summary>
    /// Подружжя, яке водночас є кровним родичем (шлюб двоюрідних):
    /// «wife (also first cousin)». Раніше кровний зв'язок перекривав факт шлюбу.
    /// </summary>
    private static string WithAlsoBlood(string name, string? bloodName) =>
        string.IsNullOrWhiteSpace(bloodName) ? name : $"{name} (also {bloodName})";

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
                return m == f ? m : $"{m} / {f}"; // «cousin» однакове для обох статей — не дублюємо
        }
    }

    private static string Build(KinshipContext c, Gender g, bool detailed) => c.Kind switch
    {
        KinshipKind.DirectAncestor => c.StepsUp == 1
            ? Pick(g, "father", "mother")
            : Great(c.StepsUp - 2) + "grand" + Pick(g, "father", "mother"),
        KinshipKind.DirectDescendant => c.StepsDown == 1
            ? Pick(g, "son", "daughter")
            : Great(c.StepsDown - 2) + "grand" + Pick(g, "son", "daughter"),
        KinshipKind.Collateral => BuildCollateral(c.StepsUp, c.StepsDown, g, c.SiblingKind, detailed),
        _ => string.Empty,
    };

    private static string BuildCollateral(int a, int b, Gender g, SiblingKind siblingKind, bool detailed)
    {
        var k = Math.Min(a, b);
        var d = Math.Abs(a - b);

        if (d == 0)
        {
            if (k == 1)
            {
                var word = Pick(g, "brother", "sister");
                return siblingKind switch
                {
                    SiblingKind.HalfPaternal or SiblingKind.HalfMaternal or SiblingKind.HalfUnknown => "half-" + word,
                    // Другий батько невідомий хоча б в однієї особи — не стверджуємо «half-».
                    SiblingKind.PossiblyHalf => detailed ? word + " (possibly half)" : word,
                    _ => word,
                };
            }

            return $"{Ordinal(k - 1)} cousin";
        }

        if (k == 1)
        {
            return b < a
                ? LateralWord(d, Pick(g, "uncle", "aunt"))   // старша гілка
                : LateralWord(d, Pick(g, "nephew", "niece")); // молодша гілка
        }

        // Кузени з різницею поколінь: «first cousin once removed».
        return $"{Ordinal(k - 1)} cousin {Removed(d)} removed";
    }

    // d=1 → uncle; d=2 → granduncle; d≥3 → great-…-granduncle
    private static string LateralWord(int d, string baseWord) =>
        d == 1 ? baseWord : Great(d - 2) + "grand" + baseWord;

    private static string WithLineage(string name, KinshipContext c)
    {
        if (c.Kind != KinshipKind.Collateral || c.StepsUp < 2)
        {
            return name;
        }

        return c.Lineage switch
        {
            Lineage.Paternal => name + " (paternal)",
            Lineage.Maternal => name + " (maternal)",
            _ => name,
        };
    }

    /// <summary>
    /// Свояцтво (розд. 4.5). Англійська система «-in-law» не розрізняє бік родини,
    /// тому не потребує статі сполучної особи (окрім описового uncle/aunt by marriage).
    /// Стать особи-B передається явно, щоб для Gender.Unknown ByGender показав обидва варіанти.
    /// </summary>
    private static string BuildAffinity(KinshipContext c, Gender g)
    {
        var name = AffinityName(c, g);

        // Свояцтво тримається на шлюбі; якщо той шлюб розірвано — «former mother-in-law».
        // Раніше IsFormerSpouse для свояцтва було зашито в false.
        return c.IsFormerSpouse ? "former " + name : name;
    }

    private static string AffinityName(KinshipContext c, Gender g) => c.Affinity switch
    {
        // «-in-law» тут не вживається: англійська для нерідних батьків/дітей
        // використовує саме «step-».
        AffinityKind.StepParent => Pick(g, "stepfather", "stepmother"),
        AffinityKind.StepChild => Pick(g, "stepson", "stepdaughter"),
        AffinityKind.SpouseParent => Pick(g, "father-in-law", "mother-in-law"),
        AffinityKind.ChildSpouse => Pick(g, "son-in-law", "daughter-in-law"),
        AffinityKind.SpouseSibling => Pick(g, "brother-in-law", "sister-in-law"),
        AffinityKind.SiblingSpouse => Pick(g, "brother-in-law", "sister-in-law"),
        AffinityKind.UncleAuntSpouse => Pick(g, "uncle (by marriage)", "aunt (by marriage)"),
        _ => "relative by marriage",
    };

    private static string Pick(Gender g, string male, string female) =>
        g == Gender.Female ? female : male;

    private static string Great(int times) => string.Concat(Enumerable.Repeat("great-", Math.Max(times, 0)));

    private static string Ordinal(int n) =>
        n >= 0 && n < Ordinals.Length ? Ordinals[n] : $"{n}th";

    private static string Removed(int d) => d switch
    {
        1 => "once",
        2 => "twice",
        3 => "thrice",
        _ => $"{d} times",
    };
}
