namespace FamilyTree.App.Localization;

/// <summary>
/// Формат файлу перекладу для додаткової мови/діалекту.
/// Кладеться у %AppData%\FamilyTree\languages\&lt;code&gt;.json, де &lt;code&gt; —
/// ідентифікатор мови (ім'я файлу без розширення), напр. "elvish.json" або "uk-poltava.json".
/// Дозволяє додавати мови, для яких немає системної CultureInfo.
/// </summary>
public sealed class CustomLanguageFile
{
    /// <summary>Назва мови для показу в перемикачі. Якщо порожня — береться код.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Необов'язкова культура для форматування дат/чисел (напр. "uk", "en").
    /// Якщо порожня чи невідома — застосовується запасна культура за замовчуванням.
    /// </summary>
    public string? FormattingCulture { get; set; }

    /// <summary>Пари «ключ ресурсу → переклад». Відсутні ключі беруться з нейтральної мови.</summary>
    public Dictionary<string, string> Strings { get; set; } = new();
}
