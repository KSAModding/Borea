namespace Borea.Core.Settings;

/// <summary>
/// Persists Borea's own settings.
/// </summary>
public interface IBoreaSettingsRepository
{
    /// <summary>
    /// Returns the current BoreaSettings, or null if no settings have been saved yet.
    /// </summary>
    Task<BoreaSettings?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(BoreaSettings settings, CancellationToken cancellationToken = default);
}
