using Borea.Core.Mods;

namespace Borea.Core.Instances;

public enum GameSaveKind
{
    Save,
    Vehicle,
}

public static class GameSaveKindExtensions
{
    public static ContentType ToContentType(this GameSaveKind kind) => kind == GameSaveKind.Save ? ContentType.Save : ContentType.Vehicle;
}
