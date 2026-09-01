using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.State;
using Borea.Storage.Toml;

namespace Borea.Storage.State;

/// <summary>
/// File-backed <see cref="IModStateRepository"/> over the instance's
/// manifest.toml. ModManifest.Save regenerates that file from the game's own
/// list and emits only id and enabled, so Borea keeps nothing of its own in it
/// and re-reads before every write.
/// </summary>
public sealed class FileModStateRepository : IModStateRepository
{
    private const string ModDefinitionFileName = "mod.toml";

    private readonly IGamePathProvider _pathProvider;

    public FileModStateRepository(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public async Task<IReadOnlyList<ModManifestEntry>> GetEntriesAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(instanceId, cancellationToken).ConfigureAwait(false);
        return manifest.Mods.Select(m => new ModManifestEntry(m.Id, m.Enabled)).ToList();
    }

    public async Task<bool> IsActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
    {
        RequireLookupId(modId);

        var manifest = await ReadManifestAsync(instanceId, cancellationToken).ConfigureAwait(false);

        return Matches(manifest, modId).Any(m => m.Enabled);
    }

    public async Task<IReadOnlyList<string>> GetAllActiveModIdsAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(instanceId, cancellationToken).ConfigureAwait(false);

        // One id per mod, the way IsActiveAsync counts them: two enabled case
        // variants are one loaded mod, and a blank id names none.
        return manifest.Mods
            .Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Id))
            .Select(m => m.Id)
            .Distinct(ModIds.Comparer)
            .ToList();
    }

    public async Task<ModEntryAddResult> AddEntryAsync(Guid instanceId, string modId, bool enabled, CancellationToken cancellationToken = default)
    {
        // ModManifest.Save writes the id unescaped, so a quote corrupts the file.
        ModIds.Validate(modId, nameof(modId));

        var modFolder = ResolveModFolder(instanceId, modId);
        if (modFolder is null)
            return ModEntryAddResult.NotOnDisk;

        var manifest = await ReadManifestAsync(instanceId, cancellationToken).ConfigureAwait(false);

        if (Matches(manifest, modId).Any())
            return ModEntryAddResult.AlreadyListed;

        // the game builds paths from the id
        // (ModEntry.Exists) and compares ids ordinally (ModLibrary.AddMods), so a
        // case mismatch is a duplicate entry on Windows and a deleted one
        // elsewhere. Valid by construction, since it equals a validated id apart
        // from case and every ModIds rule ignores case.
        var folderName = Path.GetFileName(modFolder);

        manifest.Mods.Add(new ModManifestEntryDto { Id = folderName, Enabled = enabled });

        await WriteManifestAsync(instanceId, manifest, cancellationToken).ConfigureAwait(false);
        return ModEntryAddResult.Added;
    }

    public Task<bool> SetActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
        => SetEnabledAsync(instanceId, modId, enabled: true, cancellationToken);

    public Task<bool> SetInactiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
        => SetEnabledAsync(instanceId, modId, enabled: false, cancellationToken);

    public async Task<bool> ReorderAsync(Guid instanceId, IReadOnlyList<string> modIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modIds);

        var manifest = await ReadManifestAsync(instanceId, cancellationToken).ConfigureAwait(false);

        // An entry naming no mod cannot be placed by a caller and carries no load
        // order: ModLibrary.PrepareManifest removes it on the next launch. It
        // keeps its index, rather than making the manifest unorderable.
        var pinned = new List<(int Index, ModManifestEntryDto Entry)>();
        var remaining = new List<ModManifestEntryDto>();

        for (var i = 0; i < manifest.Mods.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(manifest.Mods[i].Id))
                pinned.Add((i, manifest.Mods[i]));
            else
                remaining.Add(manifest.Mods[i]);
        }

        var reordered = new List<ModManifestEntryDto>(manifest.Mods.Count);

        foreach (var modId in modIds)
        {
            var index = remaining.FindIndex(m => ModIds.Equals(m.Id, modId));
            if (index < 0)
            {
                throw new ArgumentException(
                    $"The manifest has no entry left to place for '{modId}'.", nameof(modIds));
            }

            reordered.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        if (remaining.Count > 0)
        {
            throw new ArgumentException(
                $"The order leaves out {remaining.Count} manifest entries, starting with '{remaining[0].Id}'.",
                nameof(modIds));
        }

        foreach (var (index, entry) in pinned)
            reordered.Insert(index, entry);

        if (reordered.SequenceEqual(manifest.Mods, ReferenceEqualityComparer.Instance))
            return false;

        manifest.Mods = reordered;
        await WriteManifestAsync(instanceId, manifest, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> SetEnabledAsync(Guid instanceId, string modId, bool enabled, CancellationToken cancellationToken)
    {
        RequireLookupId(modId);

        var manifest = await ReadManifestAsync(instanceId, cancellationToken).ConfigureAwait(false);
        var matches = Matches(manifest, modId).ToList();

        var enabledMatches = matches.Where(m => m.Enabled).ToList();

        if (enabled)
        {
            if (matches.Count == 0 || enabledMatches.Count > 0)
                return false;

            // SerializedCollection.Register drops the second mod of equal
            // KeyHash, so enabling both would switch on a deliberate off.
            matches[0].Enabled = true;
        }
        else
        {
            if (enabledMatches.Count == 0)
                return false;

            // All of them, because any enabled entry loads the mod.
            foreach (var match in enabledMatches)
                match.Enabled = false;
        }

        await WriteManifestAsync(instanceId, manifest, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Every entry naming the mod, in file order. ModLibrary.AddMods compares ids
    /// ordinally so a manifest can hold two case variants, while KeyHash.Make
    /// lowercases, which makes them one mod downstream.
    /// </summary>
    private static IEnumerable<ModManifestEntryDto> Matches(ManifestDto manifest, string modId)
        => manifest.Mods.Where(m => ModIds.Equals(m.Id, modId));

    /// <summary>
    /// The mod's folder, or null when the mod is not there. The mod.toml decides,
    /// not the folder (ModLibrary.AddMods, ModEntry.Exists). Only the instance's
    /// mods folder is searched. Resolved by comparison, since ids ignore case and Linux paths
    /// do not.
    /// </summary>
    private string? ResolveModFolder(Guid instanceId, string modId)
    {
        var modsFolder = _pathProvider.GetInstanceModsFolder(instanceId);
        if (!Directory.Exists(modsFolder))
            return null;

        var modFolder = Directory.EnumerateDirectories(modsFolder)
            .FirstOrDefault(d => ModIds.Equals(Path.GetFileName(d), modId));

        if (modFolder is null)
            return null;

        return File.Exists(Path.Combine(modFolder, ModDefinitionFileName)) ? modFolder : null;
    }

    /// <summary>
    /// Weaker than the ModIds.Validate AddEntryAsync uses, because a lookup has to
    /// reach entries the game wrote, Core among them.
    /// </summary>
    private static void RequireLookupId(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));
    }

    private async Task<ManifestDto> ReadManifestAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var path = _pathProvider.GetInstanceManifestPath(instanceId);
        var manifest = await TomlFileStore.ReadAsync<ManifestDto>(path, cancellationToken).ConfigureAwait(false);
        return manifest ?? new ManifestDto();
    }

    private Task WriteManifestAsync(Guid instanceId, ManifestDto manifest, CancellationToken cancellationToken)
    {
        var path = _pathProvider.GetInstanceManifestPath(instanceId);
        return TomlFileStore.WriteAsync(path, manifest, cancellationToken);
    }
}
