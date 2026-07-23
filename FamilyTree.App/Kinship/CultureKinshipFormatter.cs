using FamilyTree.App.Localization;
using FamilyTree.Domain.Kinship;

namespace FamilyTree.App.Kinship;

/// <summary>
/// Обирає форматер назв родства за поточною мовою застосунку (розд. 2.4, 4.7):
/// "en" → англійський, решта → український. Стиль назв форвардиться в обидва.
/// </summary>
public sealed class CultureKinshipFormatter : IKinshipFormatter
{
    private readonly ILocalizationService _localization;
    private readonly UkrainianKinshipFormatter _uk = new();
    private readonly EnglishKinshipFormatter _en = new();

    public CultureKinshipFormatter(ILocalizationService localization)
    {
        _localization = localization;
    }

    private IKinshipFormatter Active =>
        string.Equals(_localization.CurrentLanguage.Code, "en", StringComparison.OrdinalIgnoreCase)
            ? _en
            : _uk;

    public string CultureCode => Active.CultureCode;

    public KinshipNamingStyle Style
    {
        get => _uk.Style;
        set
        {
            _uk.Style = value;
            _en.Style = value;
        }
    }

    public string Format(in KinshipContext context) => Active.Format(in context);
}
