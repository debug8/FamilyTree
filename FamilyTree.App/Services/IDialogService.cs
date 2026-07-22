using FamilyTree.App.ViewModels;

namespace FamilyTree.App.Services;

/// <summary>
/// Показ модальних діалогів із ViewModel-ів (без прямої залежності від Window).
/// </summary>
public interface IDialogService
{
    /// <summary>Показує редактор особи. Повертає true, якщо користувач зберіг зміни.</summary>
    bool ShowPersonEditor(PersonEditorViewModel viewModel);

    /// <summary>Показує запит підтвердження (Так/Ні).</summary>
    bool Confirm(string message, string title);
}
