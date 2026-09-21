namespace Borea.Core.Files;

/// <summary>
/// The outcome of a link attempt. When <see cref="Linked"/> is false the caller
/// must copy the files instead, and <see cref="Reason"/> says what stopped the
/// link.
/// </summary>
public sealed record DirectoryLinkResult(bool Linked, string? Reason)
{
    public static DirectoryLinkResult Created { get; } = new(true, null);

    public static DirectoryLinkResult NotLinked(string reason) => new(false, reason);
}
