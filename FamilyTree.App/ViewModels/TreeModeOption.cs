using FamilyTree.App.Localization;
using FamilyTree.Domain.Layout;

namespace FamilyTree.App.ViewModels;

/// <summary>Пункт вибору режиму дерева для UI.</summary>
public sealed class TreeModeOption : LocalizedOption
{
    public TreeModeOption(TreeMode value, string nameKey)
        : base(nameKey) => Value = value;

    public TreeMode Value { get; }
}
