namespace FamilyTree.App.ViewModels;

/// <summary>
/// Напівпрозора горизонтальна смуга-фон для одного покоління (рядка) дерева.
/// Малюється позаду всього іншого; колір різний для сусідніх поколінь.
/// </summary>
public sealed record GenerationBandViewModel(double X, double Y, double Width, double Height, string Fill);
