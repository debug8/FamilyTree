namespace FamilyTree.Domain.Kinship;

/// <summary>
/// Усе, що потрібно форматеру, щоб згенерувати назву мовою користувача.
/// </summary>
/// <param name="Kind">Тип зв'язку.</param>
/// <param name="StepsUp">a — кроки від кореневої особи до спільного предка.</param>
/// <param name="StepsDown">b — кроки від родича до спільного предка.</param>
/// <param name="RelativeGender">Стать особи, яку називаємо.</param>
/// <param name="SiblingKind">Уточнення рідні/зведені для сиблінгів.</param>
/// <param name="Lineage">Лінія (по батькові/по матері) — для детального стилю.</param>
/// <param name="IsFormerSpouse">Чи це колишнє подружжя (шлюб розірвано).</param>
/// <param name="Affinity">Різновид свояцтва (для <see cref="KinshipKind.Affinity"/>).</param>
/// <param name="PivotGender">Стать сполучної особи X (подружжя/сиблінг/дядько) — для свояцтва.</param>
public readonly record struct KinshipContext(
    KinshipKind Kind,
    int StepsUp,
    int StepsDown,
    Gender RelativeGender,
    SiblingKind SiblingKind,
    Lineage Lineage,
    bool IsFormerSpouse = false,
    AffinityKind Affinity = AffinityKind.NotAffinity,
    Gender PivotGender = Gender.Unknown);

/// <summary>
/// Форматер назв родинних зв'язків — по одному на мову (розд. 2.4, 4.3, 4.7).
/// Алгоритм (структура зв'язку в <see cref="KinshipContext"/>) спільний для всіх мов;
/// мовоспецифічна лише генерація назви за цим контекстом.
/// </summary>
public interface IKinshipFormatter
{
    /// <summary>Код культури, яку обслуговує форматер (напр. "uk", "en"). Для DI-фабрики за культурою.</summary>
    string CultureCode { get; }

    /// <summary>Стиль назв (стандартні / детальні з лінією). Керується з UI.</summary>
    KinshipNamingStyle Style { get; set; }

    /// <summary>Формує назву зв'язку за контекстом.</summary>
    string Format(in KinshipContext context);
}
