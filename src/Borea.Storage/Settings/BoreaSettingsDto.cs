namespace Borea.Storage.Settings;

public sealed class BoreaSettingsDto
{
    public string? GameDirectoryPath { get; set; }

    /// <summary>
    /// Loader id to installation record. Absent when none, so no empty table is
    /// written. Keep last because TOML puts each later key below this table.
    /// </summary>
    public Dictionary<string, LoaderInstallationDto>? LoaderInstallations { get; set; }
}

public sealed class LoaderInstallationDto
{
    public string DirectoryPath { get; set; } = string.Empty;

    public string? Version { get; set; }

    public string? RawVersion { get; set; }

    public bool IsAdopted { get; set; }
}
