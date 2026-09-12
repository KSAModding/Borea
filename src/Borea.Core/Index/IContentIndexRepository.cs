using Borea.Core.Mods;

namespace Borea.Core.Index;

/// <summary>Serves installable index content and exposes snapshot diagnostics.</summary>
public interface IContentIndexRepository : IModRepository
{
    Task<IReadOnlyList<ContentIndexDiagnostic>> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}
