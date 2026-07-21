using System.Globalization;

namespace FamilyTree.App.Localization;

/// <summary>
/// Опис однієї мови інтерфейсу.
/// </summary>
/// <param name="Code">
/// Власний ідентифікатор мови застосунку (напр. "uk", "en", "chr", "qaa-x-elvish").
/// Використовується як ключ у налаштуваннях і як ім'я перекладу. НЕ виводиться з
/// <see cref="CultureInfo.TwoLetterISOLanguageName"/> — це окрема, стабільна величина.
/// </param>
/// <param name="DisplayName">Назва мови для показу в UI (рідною мовою).</param>
/// <param name="FormattingCulture">
/// Культура для форматування дат і чисел. Для мов без власної <see cref="CultureInfo"/>
/// сюди підставляється запасна культура (за замовчуванням — мова інтерфейсу за замовчуванням).
/// </param>
public sealed record LanguageOption(string Code, string DisplayName, CultureInfo FormattingCulture);

/// <summary>
/// Сервіс локалізації: доступ до рядків UI за ключем, поточна мова,
/// перемикання з негайним сповіщенням підписників (жива зміна без перезапуску).
/// Ідентифікація мови — за власним кодом, а не за <see cref="CultureInfo"/>,
/// тож можна додавати мови/діалекти, для яких CultureInfo не існує.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Поточна активна мова.</summary>
    LanguageOption CurrentLanguage { get; }

    /// <summary>Культура для форматування дат/чисел (== <see cref="LanguageOption.FormattingCulture"/> поточної мови).</summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>Список доступних мов (вбудовані + підхоплені з файлів).</summary>
    IReadOnlyList<LanguageOption> AvailableLanguages { get; }

    /// <summary>Повертає локалізований рядок за ключем для поточної мови.</summary>
    string GetString(string key);

    /// <summary>
    /// Перемикає мову за кодом. Невідомий чи некоректний код не викидає виняток —
    /// відбувається тихий відкат на мову за замовчуванням. Піднімає <see cref="LanguageChanged"/>.
    /// </summary>
    void SetLanguage(string code);

    /// <summary>Спрацьовує після зміни мови. UI-проксі перечитує всі рядки.</summary>
    event EventHandler? LanguageChanged;
}
