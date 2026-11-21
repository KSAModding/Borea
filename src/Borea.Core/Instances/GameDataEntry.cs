namespace Borea.Core.Instances;

/// <summary>
/// One folder or file the game writes into an instance. A missing entry has size 0.
/// <see cref="ScreenshotCount"/> counts the pictures of a <see cref="GameDataKind.Screenshots"/> entry and is 0 for the others.
/// </summary>
public sealed record GameDataEntry(GameDataKind Kind, string Name, string Path, bool IsFolder, bool Exists, long SizeBytes, int ScreenshotCount = 0);
