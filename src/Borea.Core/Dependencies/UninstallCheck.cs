using System.Collections.ObjectModel;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Dependencies;

/// <summary>
/// Result of checking whether an installed mod can safely be uninstalled
/// from a specific instance.
/// </summary>
public sealed class UninstallCheck
{
    public Guid InstanceId { get; }
    public string ModId { get; }
    public ModVersion Version { get; }

    /// <summary>
    /// IDs of other mods within this instance whose dependency requirements
    /// this mod currently satisfies. Empty if nothing depends on it.
    /// </summary>
    public IReadOnlyList<string> DependentModIds { get; }

    /// <summary>
    /// The recorded vehicles and saves whose release requires this mod. They never block the
    /// uninstall, because the game only refuses them, so the player is told and goes on.
    /// </summary>
    public IReadOnlyList<InstalledContentRecord> DependentContent { get; }

    /// <summary>
    /// Whether this mod is currently active (enabled) per this instance's
    /// manifest.
    /// </summary>
    public bool IsActive { get; }

    public bool CanUninstall => DependentModIds.Count == 0;

    public UninstallCheck(
        Guid instanceId,
        string modId,
        ModVersion version,
        IReadOnlyList<string> dependentModIds,
        bool isActive,
        IReadOnlyList<InstalledContentRecord>? dependentContent = null)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        if (dependentModIds is null)
            throw new ArgumentNullException(nameof(dependentModIds));

        InstanceId = instanceId;
        ModId = modId;
        Version = version;
        DependentModIds = new ReadOnlyCollection<string>(dependentModIds.ToArray());
        IsActive = isActive;
        DependentContent = new ReadOnlyCollection<InstalledContentRecord>(dependentContent?.ToArray() ?? []);
    }
}
