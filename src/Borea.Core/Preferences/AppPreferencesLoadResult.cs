namespace Borea.Core.Preferences;

public enum AppPreferencesLoadStatus
{
    Loaded,
    NotFound,
    Invalid,
    Unavailable,
}

public sealed record AppPreferencesLoadResult(
    AppPreferencesLoadStatus Status,
    AppPreferences Preferences,
    string? Error = null);
