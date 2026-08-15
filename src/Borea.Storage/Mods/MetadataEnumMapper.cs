using Borea.Core.Mods;

namespace Borea.Storage.Mods;

/// <summary>
/// String forms of the closed metadata enums, spelled the way the format
/// writes them, plus the shared version parse helper. Only the spelled
/// vocabulary maps to a real member; anything else parses to Unknown.
/// An Unknown value is written back as "unknown", the original token is not
/// recoverable from disk, so the index snapshot stays the source of truth
/// for entries a client build did not understand.
/// </summary>
public static class MetadataEnumMapper
{
    public static string ToDto(ContentType type) => type switch
    {
        ContentType.Mod => "mod",
        ContentType.ModPack => "modpack",
        ContentType.ModLoader => "mod-loader",
        _ => "unknown",
    };

    public static ContentType ParseContentType(string value) => value.ToLowerInvariant() switch
    {
        "mod" => ContentType.Mod,
        "modpack" => ContentType.ModPack,
        "mod-loader" => ContentType.ModLoader,
        _ => ContentType.Unknown,
    };

    public static string ToDto(ModStatus status) => status switch
    {
        ModStatus.Active => "active",
        ModStatus.Deprecated => "deprecated",
        _ => "unknown",
    };

    public static ModStatus ParseModStatus(string value) => value.ToLowerInvariant() switch
    {
        "active" => ModStatus.Active,
        "deprecated" => ModStatus.Deprecated,
        _ => ModStatus.Unknown,
    };

    public static string ToDto(ReleaseStatus status) => status switch
    {
        ReleaseStatus.Stable => "stable",
        ReleaseStatus.Testing => "testing",
        ReleaseStatus.Dev => "dev",
        _ => "unknown",
    };

    public static ReleaseStatus ParseReleaseStatus(string value) => value.ToLowerInvariant() switch
    {
        "stable" => ReleaseStatus.Stable,
        "testing" => ReleaseStatus.Testing,
        "dev" => ReleaseStatus.Dev,
        _ => ReleaseStatus.Unknown,
    };

    public static string ToDto(Core.Dependencies.ModDependencyKind kind) => kind switch
    {
        Core.Dependencies.ModDependencyKind.Required => "required",
        Core.Dependencies.ModDependencyKind.Optional => "optional",
        Core.Dependencies.ModDependencyKind.Recommends => "recommends",
        Core.Dependencies.ModDependencyKind.Suggests => "suggests",
        Core.Dependencies.ModDependencyKind.Conflict => "conflict",
        _ => "unknown",
    };

    public static Core.Dependencies.ModDependencyKind ParseKind(string value) => value.ToLowerInvariant() switch
    {
        "required" => Core.Dependencies.ModDependencyKind.Required,
        "optional" => Core.Dependencies.ModDependencyKind.Optional,
        "recommends" => Core.Dependencies.ModDependencyKind.Recommends,
        "suggests" => Core.Dependencies.ModDependencyKind.Suggests,
        "conflict" => Core.Dependencies.ModDependencyKind.Conflict,
        _ => Core.Dependencies.ModDependencyKind.Unknown,
    };

    public static string ToDto(InstallAnchor anchor) => anchor switch
    {
        InstallAnchor.Mods => "mods",
        InstallAnchor.UserData => "user-data",
        InstallAnchor.GameRoot => "game-root",
        InstallAnchor.Standalone => "standalone",
        _ => "unknown",
    };

    public static InstallAnchor ParseAnchor(string value) => value.ToLowerInvariant() switch
    {
        "mods" => InstallAnchor.Mods,
        "user-data" => InstallAnchor.UserData,
        "game-root" => InstallAnchor.GameRoot,
        "standalone" => InstallAnchor.Standalone,
        _ => InstallAnchor.Unknown,
    };

    public static string? ToDto(MetadataSource? source) => source switch
    {
        null => null,
        MetadataSource.Authored => "authored",
        MetadataSource.Derived => "derived",
        _ => "unknown",
    };

    public static MetadataSource? ParseSource(string? value) => value?.ToLowerInvariant() switch
    {
        null => null,
        "authored" => MetadataSource.Authored,
        "derived" => MetadataSource.Derived,
        _ => MetadataSource.Unknown,
    };

    /// <summary>
    /// Parses an optional version string, null staying null.
    /// </summary>
    public static ModVersion? ParseVersion(string? value) =>
        value is null ? null : ModVersion.Parse(value);
}
