using FamilyTree.Domain.Kinship;

namespace FamilyTree.App.ViewModels;

/// <summary>
/// Пункт вибору стилю назв родства для UI.
/// </summary>
/// <param name="Style">Стиль форматера.</param>
/// <param name="NameKey">Ключ ресурсу з локалізованою назвою (Naming_Standard/Naming_Detailed).</param>
public sealed record KinshipNamingStyleOption(KinshipNamingStyle Style, string NameKey);
