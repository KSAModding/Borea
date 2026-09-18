using Borea.Core.Settings;

namespace Borea.Core.Logging;

public sealed class LoggingLibraryFolderChanger : ILibraryFolderChanger
{
    private readonly IBoreaLog _log;

    public ILibraryFolderChanger Inner { get; }

    public LoggingLibraryFolderChanger(ILibraryFolderChanger inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<LibraryFolderChangeResult> ChangeAsync(
        string? folder,
        IProgress<LibraryMoveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var target = folder ?? "the default folder";
        _log.Write($"Library folder change to {target} started.");

        LibraryFolderChangeResult result;
        try
        {
            result = await Inner.ChangeAsync(folder, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log.Write($"Library folder change to {target} was cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            _log.Write($"Library folder change to {target} failed.", exception);
            throw;
        }

        _log.Write(result.Changed
            ? $"Library folder change to {result.Folder} finished, {result.Outcome}{(result.OldFilesRemain ? $", old files remain in {result.PreviousFolder}" : "")}."
            : $"Library folder change to {target} refused, {result.Outcome}: {result.Message}");
        return result;
    }
}
