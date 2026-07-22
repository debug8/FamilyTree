namespace FamilyTree.App.ViewModels;

/// <summary>Роль зв'язку, який додають до обраної (базової) особи.</summary>
public enum RelationshipRole
{
    /// <summary>Додати батька/матір базовій особі.</summary>
    Parent,

    /// <summary>Додати дитину базовій особі.</summary>
    Child,

    /// <summary>Додати подружжя базовій особі.</summary>
    Spouse,
}
