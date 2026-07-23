namespace FamilyTree.App.ViewModels;

/// <summary>Ребро дерева (лінія між центрами вузлів).</summary>
public sealed record TreeEdgeViewModel(double X1, double Y1, double X2, double Y2, bool IsSpouse);
