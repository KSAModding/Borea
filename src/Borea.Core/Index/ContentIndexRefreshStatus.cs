namespace Borea.Core.Index;

/// <summary>How the last refresh of the cached content index went.</summary>
/// <param name="CachedAt">When the cached index was last fetched successfully. Null when there is no cached index.</param>
/// <param name="FailureReason">Why the last refresh failed. Null unless it failed.</param>
public sealed record ContentIndexRefreshStatus(
    ContentIndexRefreshOutcome Outcome,
    DateTimeOffset? CachedAt,
    string? FailureReason = null);
