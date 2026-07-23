using CommunityToolkit.Mvvm.ComponentModel;

namespace FamilyTree.App.Localization;

/// <summary>
/// Базовий пункт вибору з локалізованою назвою, що оновлюється живо при зміні мови.
/// <see cref="DisplayName"/> читає рядок за ключем; підписка на <see cref="LocalizationSource"/>
/// піднімає сповіщення, тож і випадний список, і поле вибору комбобокса перечитують текст.
/// </summary>
public abstract class LocalizedOption : ObservableObject
{
    protected LocalizedOption(string nameKey)
    {
        NameKey = nameKey;
        LocalizationSource.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(DisplayName));
    }

    public string NameKey { get; }

    public string DisplayName => LocalizationSource.Instance[NameKey];
}
