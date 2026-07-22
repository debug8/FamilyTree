namespace FamilyTree.Domain.Kinship;

/// <summary>
/// Результат обчислення родинного зв'язку особи-родича відносно кореневої особи (розд. 4.3).
/// </summary>
/// <param name="Kind">Тип зв'язку (предок/нащадок/бічна лінія/подружжя/немає).</param>
/// <param name="StepsUp">a — кроки вгору від кореневої особи до спільного предка.</param>
/// <param name="StepsDown">b — кроки вгору від родича до того самого предка.</param>
/// <param name="SiblingKind">Уточнення для сиблінгів (рідні/зведені).</param>
/// <param name="Lineage">Лінія спорідненості (по батькові/по матері) для бічних зв'язків.</param>
/// <param name="DisplayName">Локалізована назва зв'язку (через <see cref="IKinshipFormatter"/>).</param>
/// <param name="CommonAncestorIds">Спільні предки (для пояснення шляху — T-3.3).</param>
public sealed record KinshipResult(
    KinshipKind Kind,
    int StepsUp,
    int StepsDown,
    SiblingKind SiblingKind,
    Lineage Lineage,
    string DisplayName,
    IReadOnlyList<Guid> CommonAncestorIds);
