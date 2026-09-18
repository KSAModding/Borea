namespace Borea.Core.Instances;

/// <summary>
/// Measures what an instance takes on disk. Measured, not taken from the
/// index, so a manual install and the files a mod writes itself count too.
/// </summary>
public interface IInstanceSizeReader
{
    Task<InstanceSizes> ReadAsync(Guid instanceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The size of the whole instance folder, and of each mod folder in it by
/// folder name, which is the mod id. A folder that is gone is not listed.
/// </summary>
public sealed record InstanceSizes(long TotalBytes, IReadOnlyDictionary<string, long> ModBytes);
