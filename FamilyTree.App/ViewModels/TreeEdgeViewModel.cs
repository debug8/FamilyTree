using CommunityToolkit.Mvvm.ComponentModel;

namespace FamilyTree.App.ViewModels;

/// <summary>Ребро дерева (лінія між вузлами). Підсвічується при наведенні на джерело.</summary>
public partial class TreeEdgeViewModel : ObservableObject
{
    private static readonly IReadOnlySet<Guid> NoParents = new HashSet<Guid>();

    public TreeEdgeViewModel(
        double x1, double y1, double x2, double y2, bool isSpouse,
        IReadOnlySet<Guid>? parentIds = null, IReadOnlySet<Guid>? endpointIds = null)
    {
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
        IsSpouse = isSpouse;
        ParentIds = parentIds ?? NoParents;
        EndpointIds = endpointIds ?? NoParents;
    }

    public double X1 { get; }

    public double Y1 { get; }

    public double X2 { get; }

    public double Y2 { get; }

    public bool IsSpouse { get; }

    /// <summary>Ідентифікатори батьків-джерел ребра — для підсвітки при наведенні на особу/шлюб.</summary>
    public IReadOnlySet<Guid> ParentIds { get; }

    /// <summary>Ідентифікатори осіб на обох кінцях ребра — для підсвітки при наведенні на ребро.</summary>
    public IReadOnlySet<Guid> EndpointIds { get; }

    /// <summary>Підсвічене ребро (наведення на батька чи рамку шлюбу).</summary>
    [ObservableProperty]
    private bool _isHighlighted;
}
