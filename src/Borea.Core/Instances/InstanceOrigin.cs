namespace Borea.Core.Instances;

/// <summary>
/// How a new instance came to be. Only the Borea log uses it.
/// </summary>
public enum InstanceOrigin
{
    New,
    Duplicate,
    ModListImport,
    GameProfileImport,
    ModPack,
}
