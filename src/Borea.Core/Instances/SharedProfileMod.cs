namespace Borea.Core.Instances;

/// <summary>One mod folder of the shared profile, spelled as on disk.</summary>
public sealed record SharedProfileMod(string FolderName, bool Enabled);
