namespace FamilyTree.Domain;

/// <summary>
/// Базова доменна сутність. Ідентичність визначається за <see cref="Id"/>:
/// дві сутності рівні, якщо мають однаковий рантайм-тип і однаковий Id.
/// </summary>
public abstract class Entity : IEquatable<Entity>
{
    /// <summary>
    /// Унікальний ідентифікатор (PK). За замовчуванням — новий GUID версії 7:
    /// глобально унікальний (безпечно для злиття файлів) і впорядкований за часом
    /// створення, тож Id природно сортуються за моментом додавання.
    /// </summary>
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
