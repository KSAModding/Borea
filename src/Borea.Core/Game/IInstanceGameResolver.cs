using Borea.Core.Instances;

namespace Borea.Core.Game;

public interface IInstanceGameResolver
{
    /// <summary>
    /// The game folder and the installed version of the instance: its kept build, or the game folder of the settings
    /// when it keeps none.
    /// </summary>
    InstanceGame Resolve(Instance instance);
}
