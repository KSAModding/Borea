using Borea.Core.Instances;
using Borea.Core.Paths;
using Borea.Core.Settings;
using Borea.Storage.Files;
using Borea.Storage.Settings;

namespace Borea.Storage.Instances;

/// <summary>
/// Writes the first settings.toml of a new instance. It only reads the game's own profile and never writes into it.
/// </summary>
public sealed class NewInstanceSettings(IGamePathProvider paths, IGameSettingsPresetRepository presets)
{
    /// <summary>
    /// Copies the settings of <paramref name="source"/> into the instance. Returns null when the copy worked or there was
    /// nothing to copy, and otherwise why it failed.
    /// </summary>
    public async Task<string?> CopyAsync(Guid instanceId, InstanceSettingsSource source, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (source)
            {
                case InstanceSettingsSource.GameDefaults:
                    return null;
                case InstanceSettingsSource.FromPreset preset:
                    await presets.ApplyAsync(preset.PresetId, instanceId, cancellationToken).ConfigureAwait(false);
                    return null;
            }

            var bytes = source is InstanceSettingsSource.FromInstance from
                ? await ReadIfThereAsync(paths.GetInstanceSettingsPath(from.InstanceId), cancellationToken).ConfigureAwait(false)
                : null;
            bytes ??= await ReadIfThereAsync(SharedProfileSettings.SettingsPath(paths), cancellationToken).ConfigureAwait(false);
            if (bytes is not null)
                await AtomicFile.WriteAllBytesAsync(paths.GetInstanceSettingsPath(instanceId), bytes, cancellationToken).ConfigureAwait(false);

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return exception.Message;
        }
    }

    private static async Task<byte[]?> ReadIfThereAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}
