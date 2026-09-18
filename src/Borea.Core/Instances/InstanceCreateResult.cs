namespace Borea.Core.Instances;

/// <summary>The new instance, and whether it became the active instance because none was active.</summary>
public sealed record InstanceCreateResult(Instance Instance, bool Activated);
