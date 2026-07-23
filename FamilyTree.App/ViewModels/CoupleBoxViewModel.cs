namespace FamilyTree.App.ViewModels;

/// <summary>Рамка навколо подружжя в чинному шлюбі (малюється позаду карток осіб).</summary>
/// <param name="Tooltip">Короткий опис шлюбу (подружжя + дата одруження) для підказки.</param>
public sealed record CoupleBoxViewModel(double X, double Y, double Width, double Height, string? Tooltip = null);
