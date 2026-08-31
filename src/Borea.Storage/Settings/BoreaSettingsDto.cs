namespace Borea.Storage.Settings;

public sealed class BoreaSettingsDto
{
    public string? GameDirectoryPath { get; set; }

    /// <summary>
    /// Loader id to install directory. Absent when none, so no empty table is written.
    /// Keep last: TOML puts every key after a table header inside that table.
    /// </summary>
    public Dictionary<string, string>? LoaderDirectoryPaths { get; set; }
}
