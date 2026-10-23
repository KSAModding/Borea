namespace Borea.Storage.Instances;

public sealed class ActiveInstancePointerDto
{
    /// <summary>Null when no instance is currently selected.</summary>
    public string? ActiveInstanceId { get; set; }
}
