namespace Borea.Core.Instances;

/// <summary>
/// One folder or file the game writes into an instance. A missing entry has size 0.
/// </summary>
public sealed record GameDataEntry(string Name, string Path, bool IsFolder, bool Exists, long SizeBytes);
