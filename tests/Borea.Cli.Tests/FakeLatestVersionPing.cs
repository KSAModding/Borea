using Borea.Core.Game;

namespace Borea.Cli.Tests;

/// <summary>
/// Answers in place of the master server.
/// </summary>
internal sealed class FakeLatestVersionPing : ILatestVersionPing
{
    public const string DownloadUrl = "https://example.test/ksa";

    public LatestVersionInfo Answer { get; set; } = new(GameVersion.Parse("2026.9.7.5402"), "2026.9.7.5402", DownloadUrl);

    /// <summary>Thrown instead of answering when set.</summary>
    public Exception? Failure { get; set; }

    public Task<LatestVersionInfo> PingAsync(CancellationToken cancellationToken = default)
        => Failure is null ? Task.FromResult(Answer) : Task.FromException<LatestVersionInfo>(Failure);
}
