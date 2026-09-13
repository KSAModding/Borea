using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Cli.Output;

internal static class ContentOutput
{
    public static string Name(GameCompatibility compatibility) => compatibility switch
    {
        GameCompatibility.Compatible => "compatible",
        GameCompatibility.Untested => "untested",
        GameCompatibility.Incompatible => "incompatible",
        GameCompatibility.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(compatibility), compatibility, null),
    };

    public static string Name(ContentType type) => type switch
    {
        ContentType.Mod => "mod",
        ContentType.ModLoader => "mod-loader",
        ContentType.ModPack => "modpack",
        ContentType.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static string Name(ModStatus status) => status switch
    {
        ModStatus.Active => "active",
        ModStatus.Deprecated => "deprecated",
        ModStatus.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string Name(ReleaseStatus status) => status switch
    {
        ReleaseStatus.Stable => "stable",
        ReleaseStatus.Testing => "testing",
        ReleaseStatus.Dev => "dev",
        ReleaseStatus.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string Name(ModDependencyKind kind) => kind switch
    {
        ModDependencyKind.Required => "required",
        ModDependencyKind.Optional => "optional",
        ModDependencyKind.Recommends => "recommends",
        ModDependencyKind.Suggests => "suggests",
        ModDependencyKind.Conflict => "conflict",
        ModDependencyKind.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static string? Name(MetadataSource? source) => source switch
    {
        null => null,
        MetadataSource.Authored => "authored",
        MetadataSource.Derived => "derived",
        MetadataSource.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    public static DiagnosticView Diagnostic(ContentIndexDiagnostic diagnostic) => new(
        diagnostic.Kind switch
        {
            ContentIndexDiagnosticKind.Malformed => "malformed",
            ContentIndexDiagnosticKind.UnsupportedVersion => "unsupported-version",
            ContentIndexDiagnosticKind.UnsupportedValue => "unsupported-value",
            _ => throw new ArgumentOutOfRangeException(nameof(diagnostic), diagnostic.Kind, null),
        },
        diagnostic.Scope switch
        {
            ContentIndexDiagnosticScope.Listing => "listing",
            ContentIndexDiagnosticScope.Release => "release",
            ContentIndexDiagnosticScope.Pack => "pack",
            ContentIndexDiagnosticScope.PackVersion => "pack-version",
            ContentIndexDiagnosticScope.IndexStatus => "index-status",
            ContentIndexDiagnosticScope.GameVersions => "game-versions",
            _ => throw new ArgumentOutOfRangeException(nameof(diagnostic), diagnostic.Scope, null),
        },
        diagnostic.Reason,
        diagnostic.Id,
        diagnostic.Version,
        diagnostic.SpecVersion);

    public static IndexStatusView? IndexStatus(IndexStatus? status) => status is null
        ? null
        : new IndexStatusView(status.RawState, status.Since, status.Reason);

    public static void WriteDiagnostics(TextWriter output, IReadOnlyList<DiagnosticView> diagnostics)
    {
        if (diagnostics.Count == 0)
            return;

        output.WriteLine("Diagnostics:");
        foreach (var diagnostic in diagnostics)
        {
            var identity = diagnostic.Id is null ? string.Empty : $" {diagnostic.Id}";
            var version = diagnostic.Version is null ? string.Empty : $" {diagnostic.Version}";
            var specVersion = diagnostic.SpecVersion is null ? string.Empty : $" (spec version {diagnostic.SpecVersion})";
            output.WriteLine($"  {diagnostic.Kind} {diagnostic.Scope}{identity}{version}{specVersion}: {diagnostic.Reason}");
        }
    }
}

internal sealed record DiagnosticView(
    string Kind,
    string Scope,
    string Reason,
    string? Id,
    string? Version,
    int? SpecVersion);

internal sealed record IndexStatusView(string State, DateTimeOffset? Since, string? Reason);
