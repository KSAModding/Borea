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

    public bool IsFavorite { get; private set; }

    public Instance(string name, InstanceSource source) : this(Guid.NewGuid(), name, source, DateTimeOffset.UtcNow, Array.Empty<InstalledMod>())
    {
    }

    public static Instance FromExisting(Guid instanceId, string name, InstanceSource source, DateTimeOffset createdAt, IReadOnlyList<InstalledMod> mods, bool isFavorite)
        => new(instanceId, name, source, createdAt, mods, isFavorite);

    private Instance(Guid instanceId, string name, InstanceSource source, DateTimeOffset createdAt, IReadOnlyList<InstalledMod> mods, bool isFavorite = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Instance name cannot be null or whitespace.", nameof(name));

        if (mods is null)
            throw new ArgumentNullException(nameof(mods));

        InstanceId = instanceId;
        Name = name;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        CreatedAt = createdAt;
        _mods = mods.ToList();
        IsFavorite = isFavorite;

        var duplicateId = _mods
            .GroupBy(m => m.ModId, ModIds.Comparer)
            .FirstOrDefault(g => g.Count() > 1)?.Key;

        if (duplicateId is not null)
            throw new ArgumentException($"Duplicate mod '{duplicateId}' in initial mod list.", nameof(mods));
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

    public void SetFavorite(bool isFavorite) => IsFavorite = isFavorite;
}
