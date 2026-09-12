using Borea.Core.Mods;
using System.Collections.ObjectModel;

namespace Borea.Core.Instances;

/// <summary>
/// An isolated set of mods, saves, and configuration
/// that KSA and StarMap are redirected to at launch via CLA/ENV path substitution.
/// </summary>
public sealed class Instance
{
    private readonly List<InstalledMod> _mods;
    private readonly List<ForeignMod> _foreignMods;

    /// <summary>
    /// Immutable identifier assigned at creation. Used as the instance's folder name
    /// on disk, independent of <see cref="Name"/>.
    /// </summary>
    public Guid InstanceId { get; }

    /// <summary>
    /// User-facing display name. Mutable.
    /// </summary>
    public string Name { get; private set; }

    public InstanceSource Source { get; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<InstalledMod> Mods => new ReadOnlyCollection<InstalledMod>(_mods);

    public IReadOnlyList<ForeignMod> ForeignMods => new ReadOnlyCollection<ForeignMod>(_foreignMods);

    public bool IsFavorite { get; private set; }

    public Instance(string name, InstanceSource source) : this(Guid.NewGuid(), name, source, DateTimeOffset.UtcNow, Array.Empty<InstalledMod>(), Array.Empty<ForeignMod>())
    {
    }

    public static Instance FromExisting(Guid instanceId, string name, InstanceSource source, DateTimeOffset createdAt, IReadOnlyList<InstalledMod> mods, bool isFavorite)
        => new(instanceId, name, source, createdAt, mods, Array.Empty<ForeignMod>(), isFavorite);

    public static Instance FromExisting(
        Guid instanceId,
        string name,
        InstanceSource source,
        DateTimeOffset createdAt,
        IReadOnlyList<InstalledMod> mods,
        IReadOnlyList<ForeignMod> foreignMods,
        bool isFavorite)
        => new(instanceId, name, source, createdAt, mods, foreignMods, isFavorite);

    private Instance(
        Guid instanceId,
        string name,
        InstanceSource source,
        DateTimeOffset createdAt,
        IReadOnlyList<InstalledMod> mods,
        IReadOnlyList<ForeignMod> foreignMods,
        bool isFavorite = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Instance name cannot be null or whitespace.", nameof(name));

        if (mods is null)
            throw new ArgumentNullException(nameof(mods));

        if (foreignMods is null)
            throw new ArgumentNullException(nameof(foreignMods));

        InstanceId = instanceId;
        Name = name;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        CreatedAt = createdAt;
        _mods = mods.ToList();
        _foreignMods = foreignMods.ToList();
        IsFavorite = isFavorite;

        var duplicateId = _mods
            .GroupBy(m => m.ModId, ModIds.Comparer)
            .FirstOrDefault(g => g.Count() > 1)?.Key;

        if (duplicateId is not null)
            throw new ArgumentException($"Duplicate mod '{duplicateId}' in initial mod list.", nameof(mods));

        var duplicateForeignFolder = _foreignMods
            .GroupBy(m => m.FolderName, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1)?.Key;

        if (duplicateForeignFolder is not null)
            throw new ArgumentException($"Duplicate foreign mod '{duplicateForeignFolder}' in initial mod list.", nameof(foreignMods));

        var trackedForeignFolder = _foreignMods.FirstOrDefault(f => _mods.Any(m => ModIds.Equals(m.ModId, f.ModId)));
        if (trackedForeignFolder is not null)
            throw new ArgumentException($"Mod '{trackedForeignFolder.ModId}' cannot be both installed and foreign.", nameof(foreignMods));
    }

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Instance name cannot be null or whitespace.", nameof(newName));

        Name = newName;
    }

    public void AddMod(InstalledMod mod)
    {
        if (mod is null)
            throw new ArgumentNullException(nameof(mod));

        if (_mods.Any(m => ModIds.Equals(m.ModId, mod.ModId)))
            throw new InvalidOperationException($"Mod '{mod.ModId}' is already installed in this instance.");

        if (_foreignMods.Any(m => ModIds.Equals(m.ModId, mod.ModId)))
            throw new InvalidOperationException($"Mod '{mod.ModId}' is recorded as foreign in this instance.");

        _mods.Add(mod);
    }

    public bool RemoveMod(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        var existing = _mods.FirstOrDefault(m => ModIds.Equals(m.ModId, modId));
        if (existing is null)
            return false;

        _mods.Remove(existing);
        return true;
    }

    public void ReplaceForeignMods(IReadOnlyList<ForeignMod> foreignMods)
    {
        ArgumentNullException.ThrowIfNull(foreignMods);

        var duplicateFolder = foreignMods
            .GroupBy(m => m.FolderName, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicateFolder is not null)
            throw new ArgumentException($"Duplicate foreign mod '{duplicateFolder}'.", nameof(foreignMods));

        var trackedFolder = foreignMods.FirstOrDefault(f => _mods.Any(m => ModIds.Equals(m.ModId, f.ModId)));
        if (trackedFolder is not null)
            throw new ArgumentException($"Mod '{trackedFolder.ModId}' is already installed.", nameof(foreignMods));

        _foreignMods.Clear();
        _foreignMods.AddRange(foreignMods);
    }

    public void AdoptForeignMod(InstalledMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        if (mod.Ownership != ModInstallOwnership.Foreign)
            throw new ArgumentException("An adopted mod must keep foreign file ownership.", nameof(mod));

        var foreign = _foreignMods.FirstOrDefault(m => ModIds.Equals(m.ModId, mod.ModId))
            ?? throw new InvalidOperationException($"Mod '{mod.ModId}' is not recorded as foreign in this instance.");

        _foreignMods.Remove(foreign);
        _mods.Add(mod);
    }

    public void SetFavorite(bool isFavorite) => IsFavorite = isFavorite;
}
