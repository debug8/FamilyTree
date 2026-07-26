using System.Globalization;

namespace FamilyTree.Storage;

/// <summary>
/// Помилка роботи з файлом документа, придатна для показу користувачу.
/// Несе ключ локалізації (<see cref="MessageKey"/>) та аргументи, щоб шар застосунку
/// міг показати текст мовою інтерфейсу замість системного повідомлення .NET.
/// </summary>
public sealed class FamilyFileException : Exception
{
    public FamilyFileException(string messageKey, string fallbackMessage, IReadOnlyList<object?>? arguments = null, Exception? innerException = null)
        : base(fallbackMessage, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageKey);
        MessageKey = messageKey;
        Arguments = arguments ?? Array.Empty<object?>();
    }

    /// <summary>Ключ локалізації з <see cref="FileErrorKeys"/>.</summary>
    public string MessageKey { get; }

    /// <summary>Аргументи для <c>string.Format</c> над локалізованим шаблоном.</summary>
    public IReadOnlyList<object?> Arguments { get; }

    /// <summary>
    /// Створює помилку з ключем і аргументами; <see cref="Exception.Message"/> формується
    /// як нейтральний резервний текст (використовується, якщо ключа немає в resx).
    /// </summary>
    public static FamilyFileException Create(string messageKey, Exception? inner, params object?[] arguments)
    {
        var fallback = arguments.Length == 0
            ? messageKey
            : string.Format(CultureInfo.InvariantCulture, "{0}: {1}", messageKey, string.Join(", ", arguments));

        return new FamilyFileException(messageKey, fallback, arguments, inner);
    }
}
