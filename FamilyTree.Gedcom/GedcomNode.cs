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

    /// <summary>
    /// Шляхи тегів піддерева (з повторами), яких профіль <b>не</b> знає, — для підрахунку
    /// пропущених тегів у звіті. Шлях кваліфікований і рахується від цього вузла:
    /// «BAPM», «DEAT.PLAC», «RESI.ADDR.CITY».
    /// </summary>
    /// <param name="known">
    /// Чи належить шлях профілю. У вузол, який профіль не знає, обхід НЕ спускається:
    /// його підтеги — частина того самого пропущеного запису, і окремими рядками звіту
    /// вони лише засмічували б список («EDUC», а не «EDUC» + «EDUC.DATE» + «EDUC.PLAC»).
    /// </param>
    /// <remarks>
    /// Шлях, а не голе ім'я тега: раніше споживач звіряв із плоским списком самі імена,
    /// і <c>DEAT.PLAC</c> та <c>DEAT.NOTE</c> вважалися спожитими лише тому, що «PLAC»
    /// і «NOTE» зустрічаються під <c>BIRT</c> та <c>INDI</c>. Дані зникали, а звіт мовчав.
    /// </remarks>
    public IEnumerable<string> UnknownDescendantPaths(Func<string, bool> known)
    {
        ArgumentNullException.ThrowIfNull(known);

        return Walk(this, prefix: null, known);

        static IEnumerable<string> Walk(GedcomNode node, string? prefix, Func<string, bool> known)
        {
            foreach (var child in node.Children)
            {
                var path = prefix is null ? child.Tag : prefix + "." + child.Tag;

                if (!known(path))
                {
                    yield return path;
                    continue;
                }

                foreach (var deeper in Walk(child, path, known))
                {
                    yield return deeper;
                }
            }
        }
    }

    public override string ToString() =>
        Pointer is not null ? $"{Tag} -> @{Pointer}@"
        : Value is not null ? $"{Tag} = {Value}"
        : Tag;
}
