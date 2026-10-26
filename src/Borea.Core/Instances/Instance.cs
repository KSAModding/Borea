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
    private List<string> _launchArguments;

    /// <summary>
    /// Immutable identifier assigned at creation. Used as the instance's folder name
    /// on disk, independent of <see cref="Name"/>.
    /// </summary>
    public Guid InstanceId { get; }

    public string Name { get; private set; }

    public InstanceSource Source { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<InstalledMod> Mods => new ReadOnlyCollection<InstalledMod>(_mods);

    public IReadOnlyList<ForeignMod> ForeignMods => new ReadOnlyCollection<ForeignMod>(_foreignMods);

    public bool IsFavorite { get; private set; }

    /// <summary>When a launch through Borea last started the game, or null when none did.</summary>
    public DateTimeOffset? LastPlayedAt { get; private set; }

    /// <summary>The arguments every launch of this instance passes after the instance handover, in order.</summary>
    public IReadOnlyList<string> LaunchArguments => new ReadOnlyCollection<string>(_launchArguments);

    /// <summary>The mod loader, and its version, that a start of this instance uses.</summary>
    public LoaderSetting Loader { get; private set; } = LoaderSetting.Automatic;

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
        bool isFavorite,
        DateTimeOffset? lastPlayedAt = null,
        IReadOnlyList<string>? launchArguments = null)
        => new(instanceId, name, source, createdAt, mods, foreignMods, isFavorite, lastPlayedAt, launchArguments);

    private Instance(
        Guid instanceId,
        string name,
        InstanceSource source,
        DateTimeOffset createdAt,
        IReadOnlyList<InstalledMod> mods,
        IReadOnlyList<ForeignMod> foreignMods,
        bool isFavorite = false,
        DateTimeOffset? lastPlayedAt = null,
        IReadOnlyList<string>? launchArguments = null)
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
        LastPlayedAt = lastPlayedAt;
        _launchArguments = CheckedArguments(launchArguments ?? Array.Empty<string>());

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

    public void ChangeSource(InstanceSource source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
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

    public void ReplaceMod(InstalledMod replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        var index = _mods.FindIndex(mod => ModIds.Equals(mod.ModId, replacement.ModId));
        if (index < 0)
            throw new InvalidOperationException($"Mod '{replacement.ModId}' is not installed in this instance.");

        _mods[index] = replacement;
    }

    /// <summary>
    /// Pins or unpins a mod whose files Borea owns, because only those can be updated.
    /// Returns false when the mod already was in that state.
    /// </summary>
    public bool SetPinned(string modId, bool pinned)
    {
        var mod = _mods.FirstOrDefault(m => ModIds.Equals(m.ModId, modId))
            ?? throw new InvalidOperationException($"Mod '{modId}' is not installed in this instance.");

        if (mod.IsPinned == pinned)
            return false;

        if (pinned && mod.Ownership != ModInstallOwnership.Borea)
            throw new InvalidOperationException($"Borea does not own the files of '{mod.ModId}', so it never updates them and cannot pin them.");

        mod.SetPinned(pinned);
        return true;
    }

    /// <summary>
    /// Makes a mod of the pack a mod the player chose, so a pack update leaves it alone, also after the player
    /// removed it. Returns false when the mod already was detached.
    /// </summary>
    public bool DetachFromModPack(string modId)
    {
        if (Source is not InstanceSource.FromModPack pack)
            throw new InvalidOperationException($"Instance '{Name}' was not created from a mod pack.");

        var mod = _mods.FirstOrDefault(m => ModIds.Equals(m.ModId, modId))
            ?? throw new InvalidOperationException($"Mod '{modId}' is not installed in this instance.");

        if (mod.Reason != InstallReason.ModPack)
        {
            if (pack.Detached.Contains(mod.ModId))
                return false;

            throw new InvalidOperationException($"Mod '{mod.ModId}' was not installed from the mod pack of this instance.");
        }

        Source = pack.WithDetached(pack.Detached.Append(mod.ModId));
        mod.MarkAsManuallyInstalled();
        return true;
    }

    /// <summary>
    /// Makes a detached mod follow the pack again, so the next pack update moves it to the version the pack pins.
    /// A detached mod that the player removed only leaves the set. Returns false when the mod already follows the pack.
    /// </summary>
    public bool AttachToModPack(string modId)
    {
        if (Source is not InstanceSource.FromModPack pack)
            throw new InvalidOperationException($"Instance '{Name}' was not created from a mod pack.");

        var mod = _mods.FirstOrDefault(m => ModIds.Equals(m.ModId, modId));
        if (!pack.Detached.Contains(modId))
        {
            if (mod?.Reason == InstallReason.ModPack)
                return false;

            throw new InvalidOperationException($"Mod '{mod?.ModId ?? modId}' is not detached from the mod pack of this instance.");
        }

        Source = pack.WithDetached(pack.Detached.Where(id => !ModIds.Equals(id, modId)));
        mod?.MarkAsModPackMember();
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

    public void RecordPlayed(DateTimeOffset playedAt) => LastPlayedAt = playedAt;

    public void SetLaunchArguments(IReadOnlyList<string> arguments) => _launchArguments = CheckedArguments(arguments);

    public void SetLoader(LoaderSetting loader) => Loader = loader ?? throw new ArgumentNullException(nameof(loader));

    // a null character would cut the argument short on its way to the process
    private static List<string> CheckedArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Any(argument => argument is null || argument.Contains('\0')))
            throw new ArgumentException("A launch argument cannot be null or contain a null character.", nameof(arguments));

        return arguments.ToList();
    }

    /// <summary>The newer of <see cref="LastPlayedAt"/> and the last write of a game log, so a start without Borea counts too.</summary>
    public DateTimeOffset? LastPlayedWith(DateTimeOffset? gameLogWrittenAt)
        => LastPlayedAt is { } recorded && (gameLogWrittenAt is null || recorded >= gameLogWrittenAt)
            ? recorded
            : gameLogWrittenAt;
}
