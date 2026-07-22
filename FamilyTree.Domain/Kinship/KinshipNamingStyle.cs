namespace FamilyTree.Domain.Kinship;

/// <summary>
/// Стиль генерації назв родства.
/// </summary>
public enum KinshipNamingStyle
{
    /// <summary>Стандартні назви: «дядько», «тітка», «двоюрідний брат».</summary>
    Standard,

    /// <summary>Детальні: з уточненням лінії — «тітка (по матері)», «дядько (по батькові)».</summary>
    Detailed,
}
