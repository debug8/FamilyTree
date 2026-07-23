using FamilyTree.Domain.Layout;

namespace FamilyTree.App.ViewModels;

/// <summary>Пункт вибору режиму дерева для UI.</summary>
public sealed record TreeModeOption(TreeMode Value, string NameKey);
