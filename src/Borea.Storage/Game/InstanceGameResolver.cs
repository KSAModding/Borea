using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Paths;

namespace Borea.Storage.Game;

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

        if (instance.GameBuild is not { } build)
            return new InstanceGame(null, _paths.GetGameDirectoryPath(), _installedVersion.GetInstalledVersion(), KeptBuildProblem.None);

        var folder = _paths.GetGameBuildFolder(build);
        if (!Directory.Exists(folder))
            return new InstanceGame(build, null, null, KeptBuildProblem.Missing);

        var installed = InstalledGameVersionProvider.Read(folder);
        return installed?.Version == build
            ? new InstanceGame(build, folder, installed, KeptBuildProblem.None)
            : new InstanceGame(build, null, null, KeptBuildProblem.WrongVersion);
    }
}
