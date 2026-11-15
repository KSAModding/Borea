namespace Borea.Core.Instances;

/// <summary>The new instance, and whether it became the active instance because none was active.</summary>
/// <param name="SettingsError">Why the instance has no copy of its settings.toml, or null when the copy worked or there was nothing to copy.</param>
public sealed record InstanceCreateResult(Instance Instance, bool Activated, string? SettingsError = null);
