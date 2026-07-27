namespace Borea.Storage.Instances;

/// <summary>
/// Persisted pointer to the currently active instance.
/// </summary>
public sealed class ActiveInstancePointerDto
{
    /// <summary>Null when no instance is currently selected.</summary>
    public string? ActiveInstanceId { get; set; }
}