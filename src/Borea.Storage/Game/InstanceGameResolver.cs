using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Paths;

namespace Borea.Storage.Game;

/// <summary>Gives every instance the game folder of the settings and the version installed there.</summary>
public sealed class InstanceGameResolver : IInstanceGameResolver
{
    private readonly IGamePathProvider _paths;
    private readonly IInstalledGameVersionProvider _installedVersion;

    public InstanceGameResolver(IGamePathProvider paths, IInstalledGameVersionProvider installedVersion)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _installedVersion = installedVersion ?? throw new ArgumentNullException(nameof(installedVersion));
    }

    public InstanceGame Resolve(Instance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return new InstanceGame(null, _paths.GetGameDirectoryPath(), _installedVersion.GetInstalledVersion(), KeptBuildProblem.None);
    }
}
