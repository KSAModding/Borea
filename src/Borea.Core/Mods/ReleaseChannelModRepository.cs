namespace Borea.Core.Mods;

/// <summary>A repository whose newest release follows one release channel. Every other read passes through.</summary>
public sealed class ReleaseChannelModRepository : IModRepository
{
    public ReleaseChannelModRepository(IModRepository inner, ReleaseChannel channel)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (!Enum.IsDefined(channel))
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "The release channel is not defined.");

        Channel = channel;
    }

    public IModRepository Inner { get; }

    public ReleaseChannel Channel { get; }

    public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default)
        => Inner.GetAvailableModsAsync(cancellationToken);

    public Task<ModMetadata?> GetListingAsync(string modId, CancellationToken cancellationToken = default)
        => Inner.GetListingAsync(modId, cancellationToken);

    public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default)
        => Inner.GetLatestReleaseInChannelAsync(modId, Channel, cancellationToken);

    public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default)
        => Inner.GetReleaseAsync(modId, version, cancellationToken);

    public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default)
        => Inner.GetAvailableVersionsAsync(modId, cancellationToken);

    public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default)
        => Inner.SearchAsync(query, cancellationToken);
}
