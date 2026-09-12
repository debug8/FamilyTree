namespace FamilyTree.Domain;

/// <summary>
/// Базова доменна сутність. Ідентичність визначається за <see cref="Id"/>:
/// дві сутності рівні, якщо мають однаковий рантайм-тип і однаковий Id.
/// </summary>
public abstract class Entity : IEquatable<Entity>
{
    /// <summary>
    /// Унікальний ідентифікатор (PK) — GUID версії 7: глобально унікальний, тож зливати
    /// файли з різних машин безпечно.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Упорядкований лише з точністю до МІЛІСЕКУНДИ.</b> UUIDv7 — це 48 біт мітки
    /// часу й 74 випадкові біти, і <c>Guid.CreateVersion7()</c> бере випадковим зокрема
    /// <c>rand_a</c>, а не веде там лічильник (RFC 9562 це дозволяє). Тому в межах однієї
    /// мілісекунди порядок Id <b>випадковий</b> — перевірено: мільйон викликів у циклі
    /// дає 50/50 зростань і спадань. А мілісекунда — це багато: імпорт GEDCOM укладає в
    /// неї тисячі осіб, та й п'ятеро доданих підряд руками туди вкладаються.
    /// </para>
    /// <para>
    /// <b>Тому не сортувати за Id, коли потрібна хронологія</b> — для цього є
    /// <c>Person.CreatedAt</c> з роздільністю 100 нс. Сортування за Id доречне там, де
    /// треба лише стабільний, відтворюваний порядок (див. <c>GedcomFamilyBuilder</c>).
    /// </para>
    /// <para>
    /// На унікальність усе це не впливає ніяк: її тримають 74 випадкові біти, а не мітка
    /// часу. Ймовірність колізії на 1 000 ідентифікаторів ≈ 3·10⁻¹⁷.
    /// </para>
    /// </remarks>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public bool Equals(Entity? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Різні типи сутностей з однаковим Id не вважаються рівними.
        return GetType() == other.GetType() && Id.Equals(other.Id);
    }

    public override bool Equals(object? obj) => Equals(obj as Entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}
