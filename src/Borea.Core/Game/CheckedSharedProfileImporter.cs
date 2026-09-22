using Borea.Core.Instances;

namespace Borea.Core.Game;

/// <summary>
/// An <see cref="ISharedProfileImporter"/> that refuses while the profile the
/// game keeps does not have the shape Borea expects. Listing the mods stays
/// open, so the App can still show what is there and say why an import stops.
/// </summary>
public sealed class CheckedSharedProfileImporter : ISharedProfileImporter
{
    private readonly IGameShapeCheck _shape;

    public ISharedProfileImporter Inner { get; }

    public CheckedSharedProfileImporter(ISharedProfileImporter inner, IGameShapeCheck shape)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _shape = shape ?? throw new ArgumentNullException(nameof(shape));
    }

    public Task<IReadOnlyList<SharedProfileMod>> GetModsAsync(CancellationToken cancellationToken = default)
        => Inner.GetModsAsync(cancellationToken);

    /// <summary>
    /// The import reads the folders and the manifest of the game's own profile,
    /// so it asks about that profile and not about the instance it fills.
    /// </summary>
    public async Task<SharedProfileImportResult> ImportAsync(string instanceName, CancellationToken cancellationToken = default)
    {
        await GameShapeGate.RequireAsync(_shape, cancellationToken).ConfigureAwait(false);
        return await Inner.ImportAsync(instanceName, cancellationToken).ConfigureAwait(false);
    }
}
