using FamilyTree.App.ViewModels;

namespace FamilyTree.App.Services;

/// <summary>
/// Показ модальних діалогів із ViewModel-ів (без прямої залежності від Window).
/// </summary>
public interface IDialogService
{
    /// <summary>Показує редактор особи. Повертає true, якщо користувач зберіг зміни.</summary>
    bool ShowPersonEditor(PersonEditorViewModel viewModel);

    /// <summary>Показує діалог додавання зв'язку. Повертає true, якщо підтверджено.</summary>
    bool ShowRelationshipEditor(RelationshipEditorViewModel viewModel);

    /// <summary>Показує діалог налаштувань демо-родини. Повертає true, якщо підтверджено.</summary>
    bool ShowDemoFamilyEditor(DemoFamilyViewModel viewModel);

    /// <summary>Показує запит підтвердження (Так/Ні).</summary>
    bool Confirm(string message, string title);

    /// <summary>Показує інформаційне повідомлення (напр. помилку валідації).</summary>
    void ShowMessage(string message, string title);

    /// <summary>Запит про незбережені зміни (Зберегти / Не зберігати / Скасувати).</summary>
    SaveChangesResult ConfirmSaveChanges(string message, string title);

    /// <summary>Діалог відкриття файлу. Повертає шлях або null.</summary>
    string? AskOpenPath(string filter);

    /// <summary>Діалог збереження файлу. Повертає шлях або null.</summary>
    string? AskSavePath(string filter, string suggestedName);
}
