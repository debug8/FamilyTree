namespace FamilyTree.Domain.Kinship;

/// <summary>
/// Усе, що потрібно форматеру, щоб згенерувати назву мовою користувача.
/// Алгоритм (пари a,b) спільний для всіх мов; мовоспецифічна лише генерація назви.
/// </summary>
/// <param name="Kind">Тип зв'язку.</param>
/// <param name="StepsUp">a — кроки від кореневої особи до спільного предка.</param>
/// <param name="StepsDown">b — кроки від родича до спільного предка.</param>
/// <param name="RelativeGender">Стать особи, яку називаємо.</param>
/// <param name="SiblingKind">Уточнення рідні/зведені для сиблінгів.</param>
public readonly record struct KinshipContext(
    KinshipKind Kind,
    int StepsUp,
    int StepsDown,
    Gender RelativeGender,
    SiblingKind SiblingKind);

/// <summary>
/// Форматер назв родинних зв'язків — по одному на мову (розд. 2.4, 4.3, 4.7).
/// </summary>
public interface IKinshipFormatter
{
    /// <summary>Код культури, яку обслуговує форматер (напр. "uk", "en"). Для DI-фабрики за культурою.</summary>
    string CultureCode { get; }

    /// <summary>Формує назву зв'язку за контекстом.</summary>
    string Format(in KinshipContext context);
}
