using FamilyTree.App.Localization;
using FamilyTree.Domain.Kinship;

namespace FamilyTree.App.ViewModels;

/// <summary>Пункт вибору стилю назв родства для UI.</summary>
public sealed class KinshipNamingStyleOption : LocalizedOption
{
    public KinshipNamingStyleOption(KinshipNamingStyle style, string nameKey)
        : base(nameKey) => Style = style;

    public KinshipNamingStyle Style { get; }
}
