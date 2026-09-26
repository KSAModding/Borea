using Borea.Core.Game;
using Borea.Core.Logging;
using Borea.Core.Settings;

namespace Borea.Storage.Logging;

public sealed class LoggingGameSettingsPresetRepository : IGameSettingsPresetRepository
{
    private readonly IBoreaLog _log;

    public IGameSettingsPresetRepository Inner { get; }

    public LoggingGameSettingsPresetRepository(IGameSettingsPresetRepository inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task<IReadOnlyList<GameSettingsPreset>> ListAsync(CancellationToken cancellationToken = default)
        => Inner.ListAsync(cancellationToken);

    public Task<GameSettingsPreset?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Inner.GetAsync(id, cancellationToken);

    public async Task ApplyAsync(Guid presetId, Guid instanceId, CancellationToken cancellationToken = default)
    {
        try
        {
            await Inner.ApplyAsync(presetId, instanceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WriteFailure($"Applying settings preset {presetId} to instance {instanceId}", exception);
            throw;
        }

        _log.Write($"Settings preset {presetId} applied to instance {instanceId}.");
    }

    public async Task<GameSettingsPreset> SaveAsync(
        string name, GameVersion gameVersion, string sourceSettingsTomlPath, CancellationToken cancellationToken = default)
    {
        GameSettingsPreset preset;
        try
        {
            preset = await Inner.SaveAsync(name, gameVersion, sourceSettingsTomlPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WriteFailure($"Creation of settings preset \"{name}\"", exception);
            throw;
        }

        _log.Write($"Settings preset {Describe(preset)} created from {sourceSettingsTomlPath}.");
        return preset;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var described = Describe(id, await TryGetNameAsync(id, cancellationToken).ConfigureAwait(false));
        try
        {
            await Inner.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WriteFailure($"Deletion of settings preset {described}", exception);
            throw;
        }

        _log.Write($"Settings preset {described} deleted.");
    }

    // invalid source TOML or a blank name is refused with InvalidOperationException/ArgumentException, a user error that needs no stack trace
    private void WriteFailure(string action, Exception exception)
    {
        if (exception is InvalidOperationException or ArgumentException)
            _log.Write($"{action} refused: {exception.Message}");
        else
            _log.Write($"{action} failed.", exception);
    }

    // a lookup only adds detail to a line, so its failure must not change the operation
    private async Task<string?> TryGetNameAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return (await Inner.GetAsync(id, cancellationToken).ConfigureAwait(false))?.Name;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private static string Describe(GameSettingsPreset preset) => Describe(preset.Id, preset.Name);

    private static string Describe(Guid id, string? name)
        => name is null ? id.ToString() : $"\"{name}\" ({id})";
}
