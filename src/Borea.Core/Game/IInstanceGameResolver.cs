using Borea.Core.Instances;

namespace Borea.Core.Game;

public interface IInstanceGameResolver
{
    /// <summary>The game folder and the installed version that the loader of the instance starts.</summary>
    InstanceGame Resolve(Instance instance);
}
