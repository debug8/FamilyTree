namespace FamilyTree.Storage;

/// <summary>
/// Дефект у завантаженому файлі, який сховище полагодило самотужки
/// (напр. відкинуло зв'язок на неіснуючу особу). Ключ — із <see cref="FileErrorKeys"/>,
/// <see cref="Count"/> — скільки записів зачепило.
/// </summary>
/// <param name="MessageKey">Ключ локалізації для опису дефекту.</param>
/// <param name="Count">Кількість зачеплених записів.</param>
public sealed record DocumentIssue(string MessageKey, int Count);
