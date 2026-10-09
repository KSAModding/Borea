namespace Borea.Core.Stewardship;

/// <summary>
/// An amendment of release files as a person asks for it, with the values as typed, like the options of tools/amend.py.
/// <see cref="ReleaseAmendment.Create"/> resolves and checks it.
/// </summary>
public sealed record ReleaseChange
{
    /// <summary>The platforms that <see cref="Os"/> names, in the order of the release format.</summary>
    public static IReadOnlyList<string> Platforms => ReleaseAmendmentCheck.OsValues;

    /// <summary>A game build, or a month for its first build, that raises the lower game bound.</summary>
    public string? GameMin { get; init; }

    /// <summary>A game build, or a month that is over for its last build, that adds or lowers the upper game bound.</summary>
    public string? GameMax { get; init; }

    public bool Yank { get; init; }

    /// <summary>One sentence shown with the yank, and only with a yank.</summary>
    public string? YankReason { get; init; }

    public string? LoaderMin { get; init; }

    public string? LoaderMax { get; init; }

    /// <summary>Dependency entries that were missing, including a conflict.</summary>
    public IReadOnlyList<ReleaseDependencyAddition> AddedDependencies { get; init; } = [];

    /// <summary>New bounds of dependencies that the releases state, applied after <see cref="AddedDependencies"/>.</summary>
    public IReadOnlyList<ReleaseDependencyBounds> DependencyBounds { get; init; } = [];

    /// <summary>New kinds of dependencies that the releases state, applied after <see cref="DependencyBounds"/>. Only the owner changes a kind (RFC 0079).</summary>
    public IReadOnlyList<ReleaseDependencyKind> DependencyKinds { get; init; } = [];

    /// <summary>Removes the upper game bound, which only the owner does (RFC 0079).</summary>
    public bool RemoveGameMax { get; init; }

    /// <summary>Takes back a yank with its reason, which only the owner does (RFC 0079).</summary>
    public bool Unyank { get; init; }

    /// <summary>
    /// The new platforms of the releases from windows, linux and macos, or null to keep them. An empty list removes os,
    /// which means no known restriction. Only the owner changes os (RFC 0079).
    /// </summary>
    public IReadOnlyList<string>? Os { get; init; }

    /// <summary>Removes the min of the loader, which only the owner does (RFC 0079).</summary>
    public bool RemoveLoaderMin { get; init; }

    /// <summary>Removes the max of the loader, which only the owner does (RFC 0079).</summary>
    public bool RemoveLoaderMax { get; init; }

    /// <summary>Bounds that dependencies the releases state lose, applied after <see cref="DependencyBounds"/>. Only the owner removes a bound (RFC 0079).</summary>
    public IReadOnlyList<ReleaseDependencyBoundRemoval> RemovedDependencyBounds { get; init; } = [];

    /// <summary>
    /// The ids of dependencies that the listing declared by mistake, which match without regard to case. Only the owner removes one (RFC 0079),
    /// and only an authored entry, because a dependency that the archive's mod.toml declares stays. A release that does not state it stays as it is.
    /// </summary>
    public IReadOnlyList<string> RemovedDependencies { get; init; } = [];
}

/// <param name="Kind">A dependency kind as a release file writes it, such as "conflict".</param>
public sealed record ReleaseDependencyAddition(string Id, string Kind);

/// <summary>A new min, a new max or both of the dependency on <paramref name="Id"/>, which matches without regard to case.</summary>
public sealed record ReleaseDependencyBounds(string Id, string? Min, string? Max);

/// <summary>Removes the min, the max or both of the dependency on <paramref name="Id"/>, which matches without regard to case.</summary>
public sealed record ReleaseDependencyBoundRemoval(string Id, bool Min, bool Max);

/// <summary>The new kind of the dependency on <paramref name="Id"/>, which matches without regard to case.</summary>
/// <param name="Kind">A dependency kind as a release file writes it, such as "recommends".</param>
public sealed record ReleaseDependencyKind(string Id, string Kind);

/// <summary>Who makes an amendment, which decides whether it may widen a release.</summary>
public enum ReleaseAmender
{
    /// <summary>A steward acting alone, who only narrows (RFC 0031).</summary>
    Steward,

    /// <summary>The verified owner of the listing, or a steward on the owner's request, who may also widen (RFC 0079).</summary>
    Owner,
}

/// <param name="Path">The path of the release file in content-index-releases.</param>
/// <param name="Text">The new text of the file.</param>
/// <param name="Widens">Whether the change widens the release, which only the owner does.</param>
public sealed record AmendedRelease(string Path, string Text, bool Widens);
