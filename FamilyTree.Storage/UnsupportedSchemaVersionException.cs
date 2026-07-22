namespace FamilyTree.Storage;

/// <summary>
/// Файл має новішу версію схеми, ніж підтримує ця версія застосунку.
/// </summary>
public sealed class UnsupportedSchemaVersionException : Exception
{
    public UnsupportedSchemaVersionException(int fileVersion, int supportedVersion)
        : base($"Версія схеми файлу ({fileVersion}) новіша за підтримувану ({supportedVersion}). Оновіть застосунок.")
    {
        FileVersion = fileVersion;
        SupportedVersion = supportedVersion;
    }

    public int FileVersion { get; }

    public int SupportedVersion { get; }
}
