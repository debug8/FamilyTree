using System.Windows.Data;
using System.Windows.Markup;

namespace FamilyTree.App.Localization;

/// <summary>
/// Markup extension для локалізованих рядків у XAML: <c>{loc:Localize MainWindow_Title}</c>.
/// Повертає прив'язку до індексатора <see cref="LocalizationSource"/>, тож при зміні мови
/// текст оновлюється миттєво, без перезапуску вікна.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocalizeExtension : MarkupExtension
{
    public LocalizeExtension()
    {
    }

    public LocalizeExtension(string key)
    {
        Key = key;
    }

    /// <summary>Ключ ресурсу (напр. MainWindow_Title).</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationSource.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
