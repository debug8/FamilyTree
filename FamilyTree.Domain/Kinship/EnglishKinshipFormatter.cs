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
        var name = c.Kind switch
        {
            KinshipKind.SamePerson => "the same person",
            KinshipKind.None => "no relation",
            KinshipKind.Spouse => c.IsFormerSpouse
                ? Pick(c.RelativeGender, "ex-husband", "ex-wife")
                : Pick(c.RelativeGender, "husband", "wife"),
            KinshipKind.Affinity => BuildAffinity(c),
            _ => ByGender(c.RelativeGender, () => Build(c, Gender.Male), () => Build(c, Gender.Female)),
        };

        return Style == KinshipNamingStyle.Detailed ? WithLineage(name, c) : name;
    }

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

    private static string Build(KinshipContext c, Gender g) => c.Kind switch
    {
        KinshipKind.DirectAncestor => c.StepsUp == 1
            ? Pick(g, "father", "mother")
            : Great(c.StepsUp - 2) + "grand" + Pick(g, "father", "mother"),
        KinshipKind.DirectDescendant => c.StepsDown == 1
            ? Pick(g, "son", "daughter")
            : Great(c.StepsDown - 2) + "grand" + Pick(g, "son", "daughter"),
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
                var word = Pick(g, "brother", "sister");
                return siblingKind is SiblingKind.HalfPaternal or SiblingKind.HalfMaternal or SiblingKind.HalfUnknown
                    ? "half-" + word
                    : word;
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
    /// </summary>
    private static string BuildAffinity(KinshipContext c)
    {
        var g = c.RelativeGender;
        return c.Affinity switch
        {
            AffinityKind.SpouseParent => Pick(g, "father-in-law", "mother-in-law"),
            AffinityKind.ChildSpouse => Pick(g, "son-in-law", "daughter-in-law"),
            AffinityKind.SpouseSibling => Pick(g, "brother-in-law", "sister-in-law"),
            AffinityKind.SiblingSpouse => Pick(g, "brother-in-law", "sister-in-law"),
            AffinityKind.UncleAuntSpouse => Pick(g, "uncle (by marriage)", "aunt (by marriage)"),
            _ => "relative by marriage",
        };
    }

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
