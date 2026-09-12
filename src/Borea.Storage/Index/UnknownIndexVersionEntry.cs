namespace Borea.Storage.Index;

/// <summary>
/// One index entry whose spec_version is newer than
/// <see cref="Borea.Core.Mods.SpecVersions.Highest"/>. The shape looks fine;
/// this build just does not know that format yet, so the entry is set aside
/// rather than treated as broken.
/// </summary>
public sealed record UnknownIndexVersionEntry(string? Id, int SpecVersion);
