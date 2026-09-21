using Borea.Core.Game;
using Borea.Core.Paths;
using Borea.Core.Settings;
using Borea.Storage.Files;
using Borea.Storage.Toml;
using Tomlyn;
using Tomlyn.Parsing;

namespace Borea.Storage.Settings;

public sealed class GameSettingsPresetRepository : IGameSettingsPresetRepository
{
    private const string MetadataFileName = "preset.toml";
    private const string SettingsFileName = "settings.toml";

    private readonly IGamePathProvider _pathProvider;

    public GameSettingsPresetRepository(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public async Task<IReadOnlyList<GameSettingsPreset>> ListAsync(CancellationToken cancellationToken = default)
    {
        var root = _pathProvider.GetGameSettingsPresetsRoot();
        if (!Directory.Exists(root))
            return [];

        var presets = new List<GameSettingsPreset>();

        foreach (var presetDir in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metadataPath = Path.Combine(presetDir, MetadataFileName);

            GameSettingsPresetDto? dto;
            try
            {
                dto = await TomlFileStore.ReadAsync<GameSettingsPresetDto>(metadataPath, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // preset.toml isn't valid TOML at all — skip this folder, per-item isolation.
                continue;
            }

            if (dto is null)
                continue;

            var preset = GameSettingsPresetMapper.FromDto(dto);
            if (preset is not null)
                presets.Add(preset);
        }

        return presets;
    }

    public async Task<GameSettingsPreset?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var metadataPath = Path.Combine(_pathProvider.GetGameSettingsPresetsRoot(), id.ToString(), MetadataFileName);
        var dto = await TomlFileStore.ReadAsync<GameSettingsPresetDto>(metadataPath, cancellationToken).ConfigureAwait(false);
        return dto is null ? null : GameSettingsPresetMapper.FromDto(dto);
    }

    public async Task ApplyAsync(Guid presetId, Guid instanceId, CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.Combine(_pathProvider.GetGameSettingsPresetsRoot(), presetId.ToString(), SettingsFileName);
        if (!File.Exists(sourcePath))
            throw new InvalidOperationException($"Settings preset '{presetId}' has no settings.toml.");

        var text = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        await AtomicFile.WriteAllTextAsync(_pathProvider.GetInstanceSettingsPath(instanceId), text, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GameSettingsPreset> SaveAsync(
        string name, GameVersion gameVersion, string sourceSettingsTomlPath, CancellationToken cancellationToken = default)
    {
        var sourceText = await File.ReadAllTextAsync(sourceSettingsTomlPath, cancellationToken).ConfigureAwait(false);

        try
        {
            SyntaxParser.ParseStrict(sourceText);
        }
        catch (TomlException exception)
        {
            throw new InvalidOperationException($"'{sourceSettingsTomlPath}' is not valid TOML. {exception.Message}", exception);
        }

        var preset = new GameSettingsPreset(Guid.NewGuid(), name, gameVersion);
        var presetDir = Path.Combine(_pathProvider.GetGameSettingsPresetsRoot(), preset.Id.ToString());
        var tempDir = presetDir + ".tmp";

        Directory.CreateDirectory(tempDir);
        await File.WriteAllTextAsync(Path.Combine(tempDir, SettingsFileName), sourceText, cancellationToken).ConfigureAwait(false);
        await TomlFileStore.WriteAsync(Path.Combine(tempDir, MetadataFileName), GameSettingsPresetMapper.ToDto(preset), cancellationToken).ConfigureAwait(false);

        Directory.Move(tempDir, presetDir);
        return preset;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var presetDir = Path.Combine(_pathProvider.GetGameSettingsPresetsRoot(), id.ToString());
        if (Directory.Exists(presetDir))
            Directory.Delete(presetDir, recursive: true);

        return Task.CompletedTask;
    }
}
