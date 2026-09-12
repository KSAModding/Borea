namespace Borea.Storage.Index;

/// <summary>
/// The result of parsing one entry inside a collection where a single bad
/// item must not reject its siblings (RFC 0031's per-listing/per-release/
/// per-pack error boundary). Exactly one of <see cref="Value"/>,
/// <see cref="Unknown"/>, or <see cref="Malformed"/> is populated, matching
/// <see cref="Kind"/>.
/// </summary>
public sealed class ParseOutcome<TValue>
{
    public ParseOutcomeKind Kind { get; }
    public TValue? Value { get; }
    public UnknownIndexVersionEntry? Unknown { get; }
    public RejectedIndexEntry? Malformed { get; }

    private ParseOutcome(ParseOutcomeKind kind, TValue? value, UnknownIndexVersionEntry? unknown, RejectedIndexEntry? malformed)
    {
        Kind = kind;
        Value = value;
        Unknown = unknown;
        Malformed = malformed;
    }

    public static ParseOutcome<TValue> Valid(TValue value) => new(ParseOutcomeKind.Valid, value, null, null);

    public static ParseOutcome<TValue> NewUnknown(UnknownIndexVersionEntry entry) => new(ParseOutcomeKind.Unknown, default, entry, null);

    public static ParseOutcome<TValue> NewMalformed(RejectedIndexEntry entry) => new(ParseOutcomeKind.Malformed, default, null, entry);
}
