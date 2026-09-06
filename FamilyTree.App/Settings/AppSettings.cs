namespace FamilyTree.App.Settings;

/// <summary>
/// Користувацькі налаштування застосунку (зберігаються в settings.json у AppData).
/// На етапі T-0.2 — мова й тема; згодом (T-5.4) сюди додадуться останні файли тощо.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Код мови інтерфейсу (uk, en, ...). За замовчуванням — українська.</summary>
    public string Language { get; set; } = "uk";

    /// <summary>Код теми оформлення (brand, light, dark). За замовчуванням — фірмова.</summary>
    public string Theme { get; set; } = "brand";

    /// <summary>Стиль назв родства (standard, detailed). За замовчуванням — стандартний.</summary>
    public string KinshipNamingStyle { get; set; } = "standard";

    /// <summary>Глибина дерева за замовчуванням (кількість поколінь; 0 — усі). За замовчуванням — 3.</summary>
    public int DefaultTreeDepth { get; set; } = 3;

    /// <summary>
    /// Типовий розмір шрифта імені особи. Єдине джерело цього числа: його беруть і
    /// властивість нижче, і ресурс <c>PersonNameFontSize</c> у <c>Styles/Controls.xaml</c>
    /// (через <c>x:Static</c>), і відкат у <see cref="ClampPersonNameFontSize"/>.
    /// </summary>
    public const double DefaultPersonNameFontSize = 26;

    /// <summary>Найменший допустимий розмір шрифта імені.</summary>
    public const double MinPersonNameFontSize = 12;

    /// <summary>Найбільший допустимий розмір шрифта імені.</summary>
    public const double MaxPersonNameFontSize = 48;

    /// <summary>
    /// Розмір шрифта імені особи на вкладці «Особа».
    /// Екрана налаштувань для цього поля поки немає (правиться в settings.json);
    /// значення застосовується на старті в <c>App.OnStartup</c> через
    /// <see cref="ClampPersonNameFontSize"/>.
    /// </summary>
    public double PersonNameFontSize { get; set; } = DefaultPersonNameFontSize;

    /// <summary>
    /// Обмежує розмір шрифта імені допустимим діапазоном. Живе тут, поруч із самим
    /// полем і його межами, а не в місці застосування: settings.json правиться руками,
    /// і 0, від'ємне чи NaN зробили б ім'я невидимим — виправити його з інтерфейсу поки
    /// що ніяк.
    /// </summary>
    public static double ClampPersonNameFontSize(double value) =>
        double.IsFinite(value) && value > 0
            ? Math.Clamp(value, MinPersonNameFontSize, MaxPersonNameFontSize)
            : DefaultPersonNameFontSize;

    /// <summary>Останні відкриті файли (найновіші — першими).</summary>
    public List<string> RecentFiles { get; set; } = new();
}
