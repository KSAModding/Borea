using Borea.Core.Mods;

namespace Borea.Core.Dependencies;

/// <summary>
/// Result of checking whether an installed mod can safely be uninstalled.
/// </summary>
public sealed class UninstallCheck
{
    public string ModId { get; }
    public Mods.ModVersion Version { get; }
    public IReadOnlyList<string> DependentModIds { get; }
    public bool IsActive { get; }

    public bool CanUninstall => DependentModIds.Count == 0;

    public UninstallCheck(
        string modId,
        Mods.ModVersion version,
        IReadOnlyList<string> dependentModIds,
        bool isActive)
    {
        // ...validation...
        ModId = modId;
        Version = version;
        DependentModIds = dependentModIds;
        IsActive = isActive;
    }
}