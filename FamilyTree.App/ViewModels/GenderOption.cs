using FamilyTree.Domain;

namespace FamilyTree.App.ViewModels;

/// <summary>Пункт вибору статі для UI (значення + ключ локалізованої назви).</summary>
public sealed record GenderOption(Gender Value, string NameKey);
