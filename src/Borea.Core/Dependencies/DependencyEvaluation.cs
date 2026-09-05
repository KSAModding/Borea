using Borea.Core.Mods;

namespace Borea.Core.Dependencies;

public sealed class DependencyEvaluation
{
    public ModDependency Dependency { get; }

    public DependencyOutcome Outcome { get; }

    public string? DeclaredBy { get; }

    /// <summary>
    /// The installed mod that meets the entry or stands in the way of a conflict,
    /// which for an any_of entry names the alternative that answered it. Null when
    /// no installed mod does either.
    /// </summary>
    public string? InstalledModId { get; }

    public DependencyEvaluation(
        ModDependency dependency,
        DependencyOutcome outcome,
        string? declaredBy = null,
        string? installedModId = null)
    {
        Dependency = dependency ?? throw new ArgumentNullException(nameof(dependency));
        Outcome = outcome;
        DeclaredBy = declaredBy;
        InstalledModId = installedModId;
    }

    public override string ToString()
    {
        var declaredBy = DeclaredBy is null ? "" : $" declared by '{DeclaredBy}'";
        return $"{Outcome}: {Dependency}{declaredBy}";
    }
}
