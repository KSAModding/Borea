namespace Borea.Core.Preferences;

public interface IAppPreferencesRepository
{
    Task<AppPreferencesLoadResult> GetAsync(
        IReadOnlyCollection<string> bundledThemeNames,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        AppPreferences preferences,
        IReadOnlyCollection<string> bundledThemeNames,
        CancellationToken cancellationToken = default);
}
