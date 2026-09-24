namespace Borea.Storage.Mods;

/// <summary>The mods that were broken out of the store and are copied into each instance from then on.</summary>
public sealed class PrivateModsDto
{
    public List<string> ModIds { get; set; } = new();
}
