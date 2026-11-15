using System.Globalization;
using Borea.Core.Paths;
using Borea.Core.Settings;
using Borea.Storage.Files;
using Tomlyn;
using Tomlyn.Parsing;

namespace Borea.Storage.Settings;

/// <param name="isGameProcessRunning">Whether a KSA or mod loader process runs, which can write the game's settings.toml when it closes.</param>
public sealed class SharedProfileSettings(IGamePathProvider paths, Func<bool> isGameProcessRunning) : ISharedProfileSettings
{
    internal const string BackupFolderName = "GameSettings";

    public bool Exists() => File.Exists(SettingsPath(paths));

    /// <summary>
    /// A folder of its own under the Backups folder of the library, so the copy writes nothing but the settings.toml
    /// into the game's profile. Its name is not an instance id, so the age cleanup of the save backups skips it.
    /// </summary>
    public string BackupFolder => Path.Combine(paths.GetBackupsRoot(), BackupFolderName);

    public async Task<SharedProfileSettingsCopy> CopyFromInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        if (isGameProcessRunning())
            return new SharedProfileSettingsCopy(SharedProfileSettingsCopyOutcome.GameRunning);

        var sourcePath = paths.GetInstanceSettingsPath(instanceId);
        if (!File.Exists(sourcePath))
            return new SharedProfileSettingsCopy(SharedProfileSettingsCopyOutcome.NoInstanceSettings);

        var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        try
        {
            // the reader drops a byte order mark, as File.ReadAllText does, and the game gets the bytes as they are
            using var reader = new StreamReader(new MemoryStream(bytes));
            SyntaxParser.ParseStrict(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (TomlException exception)
        {
            throw new InvalidOperationException($"'{sourcePath}' is not valid TOML. {exception.Message}", exception);
        }

        var targetPath = SettingsPath(paths);
        var backupPath = File.Exists(targetPath) ? Backup(targetPath) : null;
        await AtomicFile.WriteAllBytesAsync(targetPath, bytes, cancellationToken).ConfigureAwait(false);
        return new SharedProfileSettingsCopy(SharedProfileSettingsCopyOutcome.Copied, backupPath);
    }

    internal static string SettingsPath(IGamePathProvider paths) => Path.Combine(paths.GetSharedProfileRoot(), "settings.toml");

    /// <summary>Copies the game's settings.toml to a new file in the backup folder, named by the UTC time of the copy.</summary>
    private string Backup(string targetPath)
    {
        var folder = BackupFolder;
        Directory.CreateDirectory(folder);
        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        for (var attempt = 1; ; attempt++)
        {
            var name = attempt == 1 ? $"settings-{stamp}.toml" : $"settings-{stamp}-{attempt}.toml";
            var backupPath = Path.Combine(folder, name);
            if (File.Exists(backupPath))
                continue;

            try
            {
                File.Copy(targetPath, backupPath, overwrite: false);
                return backupPath;
            }
            catch (IOException) when (File.Exists(backupPath))
            {
                // another copy took this name between the check and the copy, so the next attempt takes the next name
            }
        }
    }
}
