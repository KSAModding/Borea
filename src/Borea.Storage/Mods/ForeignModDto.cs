namespace Borea.Storage.Mods;

public sealed class ForeignModDto
{
    public string FolderName { get; set; } = string.Empty;
    public List<LocalModDependencyDto> Dependencies { get; set; } = new();
    public string? DependencyReadError { get; set; }
}
public sealed class LocalModDependencyDto
{
    public string ModId { get; set; } = string.Empty;
    public bool Optional { get; set; }
}
