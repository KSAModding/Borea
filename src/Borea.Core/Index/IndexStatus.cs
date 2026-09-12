using System.Globalization;

namespace Borea.Core.Index;

public sealed class IndexStatus
{
    public IndexStatusState State { get; }

    /// <summary>The state text from the index, including values this build does not know.</summary>
    public string RawState { get; }

    public DateTimeOffset? Since { get; }

    public string? Reason { get; }

    public IndexStatus(IndexStatusState state, string rawState, string? since = null, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(rawState))
            throw new ArgumentException("Index status state cannot be null or whitespace.", nameof(rawState));

        State = state;
        RawState = rawState;
        Reason = reason;
        Since = since is null ? null : ParseSince(since);
    }

    private static DateTimeOffset ParseSince(string since)
    {
        if (!since.Contains('T', StringComparison.Ordinal)
            || !DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            || parsed.Offset != TimeSpan.Zero)
            throw new FormatException($"Index status since value '{since}' must be an ISO 8601 UTC timestamp.");

        return parsed;
    }
}
