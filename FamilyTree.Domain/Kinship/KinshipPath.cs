namespace FamilyTree.Domain.Kinship;

/// <summary>
/// Пояснення шляху родства (T-3.3): ланцюжок осіб від кореневої до родича через
/// найближчого спільного предка, крокові описи та підсумковий рядок.
/// </summary>
/// <param name="PersonChain">Id осіб у порядку A → … → НСП → … → B (порожньо, якщо зв'язку немає).</param>
/// <param name="Steps">Опис кожного кроку ланцюжка (сусідні пари).</param>
/// <param name="Summary">Підсумок: хто ким є з назвою зв'язку.</param>
public sealed record KinshipPath(
    IReadOnlyList<Guid> PersonChain,
    IReadOnlyList<string> Steps,
    string Summary)
{
    public static KinshipPath NoPath(string summary) =>
        new(Array.Empty<Guid>(), Array.Empty<string>(), summary);
}
