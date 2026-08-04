namespace Borea.Network.Tests;

/// <summary>
/// A TimeProvider whose clock only moves when a test moves it.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
