namespace Borea.Core.Mods;

/// <summary>
/// Which RFC 0031 release statuses Borea offers when it picks a release itself. An exact version
/// installs from any channel.
/// </summary>
public enum ReleaseChannel
{
    /// <summary>Only stable releases. The default.</summary>
    Stable = 0,

    /// <summary>Stable and testing releases.</summary>
    Testing = 1,

    /// <summary>Every release, including an unknown status.</summary>
    Dev = 2,
}

/// <summary>
/// The one place that decides which release statuses a channel offers.
/// </summary>
public static class ReleaseChannels
{
    /// <summary>The channel names in enum order.</summary>
    public static IReadOnlyList<string> Names { get; } = ["stable", "testing", "dev"];

    /// <summary>Whether the channel offers <paramref name="status"/>. An unknown status counts as dev.</summary>
    public static bool Includes(this ReleaseChannel channel, ReleaseStatus status) => status switch
    {
        ReleaseStatus.Stable => true,
        ReleaseStatus.Testing => channel is ReleaseChannel.Testing or ReleaseChannel.Dev,
        _ => channel == ReleaseChannel.Dev,
    };

    /// <summary>The lowercase channel name.</summary>
    public static string ToName(this ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Stable => "stable",
        ReleaseChannel.Testing => "testing",
        ReleaseChannel.Dev => "dev",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "The release channel is not defined."),
    };

    /// <summary>Reads a channel name, ignoring case.</summary>
    public static bool TryParse(string? name, out ReleaseChannel channel)
    {
        for (var index = 0; index < Names.Count; index++)
        {
            if (string.Equals(Names[index], name, StringComparison.OrdinalIgnoreCase))
            {
                channel = (ReleaseChannel)index;
                return true;
            }
        }

        channel = ReleaseChannel.Stable;
        return false;
    }

    /// <summary>
    /// The newest release that is not yanked and that <paramref name="channel"/> offers, or null.
    /// A <see cref="ReleaseChannelModRepository"/> is read through its inner repository.
    /// </summary>
    public static async Task<ModVersionMetadata?> GetLatestReleaseInChannelAsync(
        this IModRepository repository,
        string modId,
        ReleaseChannel channel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        while (repository is ReleaseChannelModRepository channelled)
            repository = channelled.Inner;

        var latest = await repository.GetLatestReleaseAsync(modId, cancellationToken).ConfigureAwait(false);
        if (latest is null)
            return null;

        if (channel.Includes(latest.ReleaseStatus))
            return latest;

        var versions = await repository.GetAvailableVersionsAsync(modId, cancellationToken).ConfigureAwait(false);
        foreach (var version in versions.OrderByDescending(value => value))
        {
            var release = await repository.GetReleaseAsync(modId, version, cancellationToken).ConfigureAwait(false);
            if (release is { Yanked: false } && channel.Includes(release.ReleaseStatus))
                return release;
        }

        return null;
    }
}
