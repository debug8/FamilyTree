namespace FamilyTree.Gedcom;

/// <summary>
/// Вузол дерева записів GEDCOM: тег, значення й вкладені вузли. Рівні з файлу вже згорнуто
/// у вкладеність, а <c>CONC</c>/<c>CONT</c> — склеєно у <see cref="Value"/>, тож споживач
/// (імпортер) працює зі структурою, а не з пласким потоком рядків.
/// </summary>
public sealed class GedcomNode
{
    private readonly List<GedcomNode> _children = new();

    public GedcomNode(string tag, string? xref = null, string? value = null, string? pointer = null)
    {
        ArgumentNullException.ThrowIfNull(tag);
        Tag = tag;
        Xref = xref;
        Value = value;
        Pointer = pointer;
    }

    /// <summary>Тег у верхньому регістрі. Для кореня — порожній рядок.</summary>
    public string Tag { get; }

    /// <summary>Ідентифікатор запису без «собачок» (лише у записів рівня 0), або null.</summary>
    public string? Xref { get; }

    /// <summary>Значення вузла (уже склеєне з <c>CONC</c>/<c>CONT</c>), або null.</summary>
    public string? Value { get; internal set; }

    /// <summary>Значення-вказівник без «собачок», або null.</summary>
    public string? Pointer { get; }

    /// <summary>Вкладені вузли в порядку з файлу.</summary>
    public IReadOnlyList<GedcomNode> Children => _children;

    internal void Add(GedcomNode child) => _children.Add(child);

    /// <summary>Перший нащадок із вказаним тегом, або null.</summary>
    public GedcomNode? Child(string tag) =>
        _children.FirstOrDefault(c => string.Equals(c.Tag, tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>Усі нащадки з вказаним тегом (для повторюваних: CHIL, NAME, MARR).</summary>
    public IEnumerable<GedcomNode> ChildrenOf(string tag) =>
        _children.Where(c => string.Equals(c.Tag, tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>Значення першого нащадка з вказаним тегом, або null.</summary>
    public string? ChildValue(string tag) => Child(tag)?.Value;

    /// <summary>Вказівник першого нащадка з вказаним тегом, або null.</summary>
    public string? ChildPointer(string tag) => Child(tag)?.Pointer;

    /// <summary>
    /// Значення за шляхом тегів, напр. <c>Path("BIRT", "DATE")</c> або
    /// <c>Path("GEDC", "VERS")</c>. Порожній шлях повертає власне значення.
    /// </summary>
    public string? Path(params string[] tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var node = this;
        foreach (var tag in tags)
        {
            node = node.Child(tag);
            if (node is null)
            {
                return null;
            }
        }

        return node.Value;
    }

    /// <summary>Усі теги піддерева (з повторами) — для підрахунку пропущених тегів у звіті.</summary>
    public IEnumerable<string> DescendantTags()
    {
        foreach (var child in _children)
        {
            yield return child.Tag;

            foreach (var tag in child.DescendantTags())
            {
                yield return tag;
            }
        }
    }

    public override string ToString() =>
        Pointer is not null ? $"{Tag} -> @{Pointer}@"
        : Value is not null ? $"{Tag} = {Value}"
        : Tag;
}
