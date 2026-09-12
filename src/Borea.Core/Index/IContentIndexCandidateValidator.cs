namespace Borea.Core.Index;

/// <summary>Accepts or rejects a downloaded snapshot before it replaces the cache.</summary>
public interface IContentIndexCandidateValidator
{
    Task ValidateAsync(string candidatePath, CancellationToken cancellationToken = default);
}
