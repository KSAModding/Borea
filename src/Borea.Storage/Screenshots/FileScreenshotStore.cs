using Borea.Core.Screenshots;

namespace Borea.Storage.Screenshots;

public sealed class FileScreenshotStore : IScreenshotStore
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };

    public Task<IReadOnlyList<Screenshot>> ListAsync(string folder, CancellationToken cancellationToken = default)
        => Task.Run(() => List(folder, cancellationToken), cancellationToken);

    public Task<bool> AnyAsync(IReadOnlyList<string> folders, CancellationToken cancellationToken = default)
        => Task.Run(() => folders.Any(folder => HasImage(folder, cancellationToken)), cancellationToken);

    public Task<ScreenshotCopyResult> CopyAsync(IReadOnlyList<Screenshot> screenshots, string targetFolder, CancellationToken cancellationToken = default)
        => Task.Run(() => Copy(screenshots, targetFolder, cancellationToken), cancellationToken);

    private static IReadOnlyList<Screenshot> List(string folder, CancellationToken cancellationToken)
    {
        var screenshots = new List<Screenshot>();
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ImageExtensions.Contains(file.Extension))
                    continue;

                try
                {
                    screenshots.Add(Screenshot.FromFile(file.FullName, file.LastWriteTime, file.Length));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The file vanished or is locked since the scan reached it.
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }

        return screenshots
            .OrderByDescending(screenshot => screenshot.TakenAt)
            .ThenBy(screenshot => Path.GetFileName(screenshot.Path), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Reads only the file names and stops at the first image, because a folder can hold thousands of screenshots.</summary>
    private static bool HasImage(string folder, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ImageExtensions.Contains(Path.GetExtension(file)))
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An absent folder is an IOException too, and neither it nor a folder that cannot be read holds a screenshot to show.
        }

        return false;
    }

    private static ScreenshotCopyResult Copy(IReadOnlyList<Screenshot> screenshots, string targetFolder, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetFolder);
        foreach (var screenshot in screenshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = FreeTargetPath(targetFolder, Path.GetFileName(screenshot.Path));
            File.Copy(screenshot.Path, target, overwrite: false);
            File.SetLastWriteTime(target, File.GetLastWriteTime(screenshot.Path));
        }

        return new ScreenshotCopyResult(targetFolder, screenshots.Count);
    }

    private static string FreeTargetPath(string folder, string fileName)
    {
        var target = Path.Combine(folder, fileName);
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var number = 2; File.Exists(target); number++)
            target = Path.Combine(folder, $"{name} ({number}){extension}");

        return target;
    }
}
