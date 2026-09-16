namespace Borea.Core.Instances;

/// <summary>
/// The folder name and date stand in when meta.toml cannot be read.
/// </summary>
public sealed record GameSaveEntry(GameSaveKind Kind, string FolderName, string Path, string Name, DateTimeOffset UpdatedAt, string? GameBuild, long SizeBytes);
