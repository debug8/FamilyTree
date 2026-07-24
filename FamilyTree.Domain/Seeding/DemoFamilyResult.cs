namespace FamilyTree.Domain.Seeding;

/// <summary>
/// Результат генерації демо-родини: доменні сутності (без залежності від сховища/UI)
/// та підказка кореневої особи — того, у кого найбагатше оточення родичів
/// (є і предки, і нащадки, і бічні гілки), щоб режим «Усі родичі» одразу показав багато назв.
/// </summary>
public sealed record DemoFamilyResult(
    IReadOnlyList<Person> Persons,
    IReadOnlyList<ParentChildLink> ParentChildLinks,
    IReadOnlyList<SpouseLink> SpouseLinks,
    Guid? SuggestedRootId);
