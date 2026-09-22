namespace Borea.Core.Updates;

/// <summary>Whether the running build may replace itself.</summary>
/// <param name="PackageManager">The manager the marker file names, when one blocks the update, otherwise null.</param>
public sealed record SelfUpdateReadiness(SelfUpdateBlock Block, string? PackageManager = null)
{
    public static SelfUpdateReadiness Ready { get; } = new(SelfUpdateBlock.None);

    public bool CanUpdate => Block == SelfUpdateBlock.None;
}
