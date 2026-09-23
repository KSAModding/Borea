using System.Text.Json;
using System.Text.Json.Serialization;
using Borea.Core.Paths;
using Borea.Core.Preferences;

namespace Borea.Storage.Preferences;

public sealed class FileAppPreferencesRepository : IAppPreferencesRepository
{
    internal const int CurrentFormatVersion = 1;

    /// <summary>The closed update banner of Borea before 0.2.0, which is read and never acted on.</summary>
    private const string RetiredDismissedReleaseKey = "dismissedBoreaRelease";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    private readonly IGamePathProvider _pathProvider;

    public FileAppPreferencesRepository(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public async Task<AppPreferencesLoadResult> GetAsync(
        IReadOnlyCollection<string> bundledThemeNames,
        CancellationToken cancellationToken = default)
    {
        if (bundledThemeNames is null)
            throw new ArgumentNullException(nameof(bundledThemeNames));

        var path = _pathProvider.GetAppPreferencesPath();
        try
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(text);
            RejectDuplicateProperties(document.RootElement);

            var dto = document.Deserialize<AppPreferencesDocumentDto>(JsonOptions);
            if (dto is null)
                return Invalid(path, "The document is empty.");

            if (dto.FormatVersion != CurrentFormatVersion)
                return Invalid(path, $"Format version {dto.FormatVersion} is not supported.");

            var preferences = AppPreferencesMapper.FromDto(dto);
            preferences.ValidateBundledThemeNames(bundledThemeNames);

            if (document.RootElement.TryGetProperty(RetiredDismissedReleaseKey, out _))
                await TryRewriteAsync(preferences, bundledThemeNames, cancellationToken).ConfigureAwait(false);

            return new(AppPreferencesLoadStatus.Loaded, preferences);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            return new(AppPreferencesLoadStatus.NotFound, AppPreferences.Empty);
        }
        catch (DirectoryNotFoundException)
        {
            return new(AppPreferencesLoadStatus.NotFound, AppPreferences.Empty);
        }
        catch (JsonException exception)
        {
            return Invalid(path, exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return Invalid(path, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Invalid(path, exception.Message);
        }
        catch (IOException exception)
        {
            return Unavailable(path, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Unavailable(path, exception.Message);
        }
    }

    public async Task SaveAsync(
        AppPreferences preferences,
        IReadOnlyCollection<string> bundledThemeNames,
        CancellationToken cancellationToken = default)
    {
        if (preferences is null)
            throw new ArgumentNullException(nameof(preferences));

        preferences.ValidateBundledThemeNames(bundledThemeNames);

        var path = _pathProvider.GetAppPreferencesPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var text = JsonSerializer.Serialize(AppPreferencesMapper.ToDto(preferences), JsonOptions) + "\n";
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            await File.WriteAllTextAsync(tempPath, text, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            TryDeleteLeftover(tempPath);
        }
    }

    /// <summary>
    /// Saves a file that still holds a key Borea no longer uses, so the key goes at once
    /// instead of at the next change of a setting. A failed save leaves it for the next start.
    /// </summary>
    private async Task TryRewriteAsync(
        AppPreferences preferences,
        IReadOnlyCollection<string> bundledThemeNames,
        CancellationToken cancellationToken)
    {
        try
        {
            await SaveAsync(preferences, bundledThemeNames, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new JsonException($"Property '{property.Name}' appears more than once.");

                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                RejectDuplicateProperties(item);
        }
    }

    private static AppPreferencesLoadResult Invalid(string path, string error)
        => new(AppPreferencesLoadStatus.Invalid, AppPreferences.Empty, $"{path} is not valid app preference data. {error}");

    private static AppPreferencesLoadResult Unavailable(string path, string error)
        => new(AppPreferencesLoadStatus.Unavailable, AppPreferences.Empty, $"{path} could not be read. {error}");

    private static void TryDeleteLeftover(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
