using Borea.Core.Mods;

namespace Borea.Core.Dependencies;

public sealed record LocalDependencyEvaluation(
    LocalModDependency Dependency,
    DependencyOutcome Outcome,
    string? InstalledModId = null);
