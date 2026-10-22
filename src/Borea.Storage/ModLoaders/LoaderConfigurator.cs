using System.Text.Json;
using System.Text.Json.Nodes;
using Borea.Core.Game;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Files;
using Borea.Storage.Game;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Serialization;

namespace Borea.Storage.ModLoaders;

/// <summary>
/// The file is read as a tree, the one key is set, and the tree is written
/// back, so every other key survives. A game in a Wine prefix gets its
/// Windows path, which the loader in the prefix can open.
/// </summary>
public sealed class LoaderConfigurator : ILoaderConfigurator, ILoaderConfigurationReader
{
    private static readonly JsonDocumentOptions JsonReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Refused at the parse, where the error names the file.
        AllowDuplicateProperties = false,
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private const string GameAssemblyFileName = "KSA.dll";

    private readonly IWinePrefixProbe _wine;

    public LoaderConfigurator()
        : this(new WinePrefixProbe())
    {
    }

    public LoaderConfigurator(IWinePrefixProbe wine)
    {
        _wine = wine ?? throw new ArgumentNullException(nameof(wine));
    }

    public string GamePathValue(string gameDirectory)
    {
        var game = Path.TrimEndingDirectorySeparator(Absolute(gameDirectory, nameof(gameDirectory)));
        if (NativeLinuxBuild.IsIn(game) || _wine.Find(game) is not { } install)
            return game;

        return _wine.ToWindowsPath(install, game)
            ?? throw new InvalidOperationException(
                $"The game folder '{game}' is in the Wine prefix '{install.PrefixRoot}', and no drive of that prefix holds it, so a mod loader in the prefix could not find the game. Borea changed nothing. Give the folder a drive in the Wine configuration, or move the game to drive C.");
    }

    public async Task<string?> ConfigureAsync(
        ModMetadata loader,
        string loaderDirectory,
        string gameDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);

        if (loader.Type != ContentType.ModLoader)
            throw new ArgumentException("Only a mod loader has a configuration file to write.", nameof(loader));

        var directory = Absolute(loaderDirectory, nameof(loaderDirectory));
        _ = Absolute(gameDirectory, nameof(gameDirectory));

        var configure = loader.Provides?.Configure;
        if (configure?.GamePath is null)
            return null;

        if (configure.Format is not (ConfigureFormat.Json or ConfigureFormat.Toml))
            throw new NotSupportedException($"The listing of {loader.Name} keeps its configuration in a format this version of Borea cannot write.");

        var game = GamePathValue(gameDirectory);

        var keys = configure.GamePath.Split('.');
        var file = Path.GetFullPath(Path.Combine(directory, configure.File.Replace('/', Path.DirectorySeparatorChar)));
        var text = File.Exists(file)
            ? await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false)
            : null;

        var written = configure.Format == ConfigureFormat.Json
            ? SetJson(text, keys, game, file, loader)
            : SetToml(text, keys, game, file, loader);

        await AtomicFile.WriteAllTextAsync(file, written, cancellationToken).ConfigureAwait(false);
        return file;
    }

    public async Task<string?> RefreshForWineAsync(
        ModMetadata loader,
        string loaderDirectory,
        string gameDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);

        if (loader.Type != ContentType.ModLoader)
            throw new ArgumentException("Only a mod loader has a configuration file to write.", nameof(loader));

        var configure = loader.Provides?.Configure;
        if (configure?.GamePath is null
            || configure.Format is not (ConfigureFormat.Json or ConfigureFormat.Toml)
            || string.IsNullOrWhiteSpace(loaderDirectory) || !Path.IsPathFullyQualified(loaderDirectory)
            || string.IsNullOrWhiteSpace(gameDirectory) || !Path.IsPathFullyQualified(gameDirectory))
        {
            return null;
        }

        var directory = Path.GetFullPath(loaderDirectory);
        var game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));

        // the host starts a loader that reads the host path, so nothing is stale
        if (string.Equals(GamePathValue(game), game, StringComparison.Ordinal))
            return null;

        // only the host path of the game, which an older Borea wrote, or no value is replaced, so a value the player set stays
        var configured = await ReadRawGamePathAsync(loader, configure, directory, cancellationToken).ConfigureAwait(false);
        if (configured is not null && !string.Equals(Path.TrimEndingDirectorySeparator(configured), game, StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            return await ConfigureAsync(loader, directory, game, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException($"Borea could not write the Windows game path to the configuration of {loader.Name} in '{directory}', so {loader.Name} in the Wine prefix could not find the game. {exception.Message}", exception);
        }
    }

    public async Task<string?> PrepareForLaunchAsync(
        ModMetadata loader,
        string loaderDirectory,
        string gameDirectory,
        bool isAdopted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);

        if (loader.Type != ContentType.ModLoader)
            throw new ArgumentException("Only a mod loader has a configuration file to write.", nameof(loader));

        var configure = loader.Provides?.Configure;
        if (configure?.GamePath is null
            || configure.Format is not (ConfigureFormat.Json or ConfigureFormat.Toml)
            || string.IsNullOrWhiteSpace(loaderDirectory) || !Path.IsPathFullyQualified(loaderDirectory)
            || string.IsNullOrWhiteSpace(gameDirectory) || !Path.IsPathFullyQualified(gameDirectory))
        {
            return null;
        }

        // a write would make the folder again with only the configuration file in it, and the launcher reports the missing folder
        var directory = Path.GetFullPath(loaderDirectory);
        if (!Directory.Exists(directory))
            return null;

        var game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));
        var configured = await ReadConfiguredGamePathAsync(loader, directory, game, cancellationToken).ConfigureAwait(false);
        var assembly = GameAssemblyOf(configured, directory);

        var stale = isAdopted
            ? assembly is null || !File.Exists(assembly)
            : assembly is null || !PathComparer.Equals(assembly, Path.Combine(game, GameAssemblyFileName));
        if (!stale)
            return await RefreshForWineAsync(loader, directory, game, cancellationToken).ConfigureAwait(false);

        try
        {
            return await ConfigureAsync(loader, directory, game, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException($"Borea could not write the game path to the configuration of {loader.Name} in '{directory}', so {loader.Name} would not find the game. {exception.Message}", exception);
        }
    }

    /// <summary>
    /// The file StarMap loads for a configured value in LoaderConfig.TryLoadConfig, which appends KSA.dll to a folder and
    /// takes any other value as the file. The loader starts in its own folder, so a relative value is relative to that folder.
    /// Null when the value is absent, empty or no path.
    /// </summary>
    private static string? GameAssemblyOf(string? configured, string loaderDirectory)
    {
        if (string.IsNullOrEmpty(configured))
            return null;

        try
        {
            var path = Path.GetFullPath(configured, loaderDirectory);
            return Directory.Exists(path) ? Path.Combine(Path.TrimEndingDirectorySeparator(path), GameAssemblyFileName) : path;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public async Task<string?> ReadConfiguredGamePathAsync(
        ModMetadata loader,
        string loaderDirectory,
        string? gameDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);

        if (loader.Type != ContentType.ModLoader)
            throw new ArgumentException("Only a mod loader has a configuration file to read.", nameof(loader));

        var directory = Absolute(loaderDirectory, nameof(loaderDirectory));
        var configure = loader.Provides?.Configure;
        if (configure?.GamePath is null)
            return null;

        if (configure.Format is not (ConfigureFormat.Json or ConfigureFormat.Toml))
            throw new NotSupportedException($"The listing of {loader.Name} keeps its configuration in a format this version of Borea cannot read.");

        var configured = await ReadRawGamePathAsync(loader, configure, directory, cancellationToken).ConfigureAwait(false);
        return configured is null ? null : HostGamePath(configured, gameDirectory, directory);
    }

    /// <summary>The game path as the file holds it, or null when the file or the key is absent.</summary>
    private static async Task<string?> ReadRawGamePathAsync(ModMetadata loader, LoaderConfigure configure, string directory, CancellationToken cancellationToken)
    {
        var file = Path.GetFullPath(Path.Combine(directory, configure.File.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(file))
            return null;

        var text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var keys = configure.GamePath!.Split('.');
        return configure.Format == ConfigureFormat.Json
            ? ReadJsonGamePath(text, keys, file, loader)
            : ReadTomlGamePath(text, keys, file, loader);
    }

    /// <summary>
    /// Maps a Windows path through the prefix of the game, or else of the
    /// loader. A value that names the game directory gives the directory as
    /// Borea keeps it, because the mapped path has its links resolved and would
    /// not compare equal. Any other value stays as it is, such as the host path
    /// an older Borea wrote.
    /// </summary>
    private string HostGamePath(string configured, string? gameDirectory, string loaderDirectory)
    {
        if (!WineDriveMap.IsDrivePath(configured))
            return configured;

        if (!string.IsNullOrWhiteSpace(gameDirectory) && Path.IsPathFullyQualified(gameDirectory))
        {
            var game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));
            if (_wine.Find(game) is { } install)
            {
                if (_wine.ToWindowsPath(install, game) is { } windows
                    && string.Equals(windows.TrimEnd('\\'), configured.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                {
                    return game;
                }

                if (install.Drives.ToHost(configured) is { } host)
                    return host;
            }
        }

        return _wine.Find(loaderDirectory)?.Drives.ToHost(configured) ?? configured;
    }

    private static string? ReadJsonGamePath(string text, string[] keys, string file, ModMetadata loader)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text, documentOptions: JsonReadOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"'{file}' is not valid JSON, so Borea cannot inspect {loader.Name}. {exception.Message}", exception);
        }

        var table = node as JsonObject
            ?? throw new InvalidOperationException($"'{file}' does not hold a JSON object at its root, so Borea cannot inspect its game path.");

        for (var i = 0; i < keys.Length - 1; i++)
        {
            if (table[keys[i]] is null)
                return null;

            table = table[keys[i]] as JsonObject
                ?? throw new InvalidOperationException(
                    $"'{file}' holds a value at '{string.Join('.', keys.Take(i + 1))}' where the listing of {loader.Name} expects an object.");
        }

        var value = table[keys[^1]];
        if (value is null)
            return null;

        return value is JsonValue json && json.TryGetValue<string>(out var result)
            ? result
            : throw new InvalidOperationException($"'{file}' does not hold a string at '{string.Join('.', keys)}'.");
    }

    private static string? ReadTomlGamePath(string text, string[] keys, string file, ModMetadata loader)
    {
        var options = new TomlSerializerOptions { MetadataStore = new TomlMetadataStore() };

        TomlTable table;
        try
        {
            if (DuplicateKey(text, options) is { } duplicate)
                throw new InvalidOperationException($"'{file}' holds the key '{duplicate}' twice, so Borea cannot inspect {loader.Name} without choosing one value.");

            table = TomlSerializer.Deserialize<TomlTable>(text, options) ?? new TomlTable();
        }
        catch (TomlException exception)
        {
            throw new InvalidOperationException($"'{file}' is not valid TOML, so Borea cannot inspect {loader.Name}. {exception.Message}", exception);
        }

        for (var i = 0; i < keys.Length - 1; i++)
        {
            if (!table.TryGetValue(keys[i], out var existing))
                return null;

            table = existing as TomlTable
                ?? throw new InvalidOperationException(
                    $"'{file}' holds a value at '{string.Join('.', keys.Take(i + 1))}' where the listing of {loader.Name} expects a table.");
        }

        if (!table.TryGetValue(keys[^1], out var value))
            return null;

        return value as string
            ?? throw new InvalidOperationException($"'{file}' does not hold a string at '{string.Join('.', keys)}'.");
    }

    private static string SetJson(string? text, string[] keys, string value, string file, ModMetadata loader)
    {
        JsonObject root;
        if (string.IsNullOrWhiteSpace(text))
        {
            root = new JsonObject();
        }
        else
        {
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(text, documentOptions: JsonReadOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException($"'{file}' is not valid JSON, so Borea cannot configure {loader.Name} without destroying it. {exception.Message}", exception);
            }

            root = node as JsonObject
                ?? throw new InvalidOperationException($"'{file}' does not hold a JSON object at its root, so there is nowhere to put the game path.");
        }

        var table = root;
        for (var i = 0; i < keys.Length - 1; i++)
        {
            switch (table[keys[i]])
            {
                case JsonObject child:
                    table = child;
                    break;
                case null:
                    var created = new JsonObject();
                    table[keys[i]] = created;
                    table = created;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"'{file}' holds a value at '{string.Join('.', keys.Take(i + 1))}' where the listing of {loader.Name} expects an object.");
            }
        }

        table[keys[^1]] = value;
        return root.ToJsonString(JsonWriteOptions) + Environment.NewLine;
    }

    private static string SetToml(string? text, string[] keys, string value, string file, ModMetadata loader)
    {
        // One store for read and write keeps the comments of the file.
        var options = new TomlSerializerOptions { MetadataStore = new TomlMetadataStore() };

        TomlTable root;
        if (string.IsNullOrWhiteSpace(text))
        {
            root = new TomlTable();
        }
        else
        {
            try
            {
                // The table model keeps the last value of a repeated key.
                if (DuplicateKey(text, options) is { } duplicate)
                    throw new InvalidOperationException($"'{file}' holds the key '{duplicate}' twice, so Borea cannot configure {loader.Name} without dropping one of them.");

                root = TomlSerializer.Deserialize<TomlTable>(text, options) ?? new TomlTable();
            }
            catch (TomlException exception)
            {
                throw new InvalidOperationException($"'{file}' is not valid TOML, so Borea cannot configure {loader.Name} without destroying it. {exception.Message}", exception);
            }
        }

        var table = root;
        for (var i = 0; i < keys.Length - 1; i++)
        {
            if (table.TryGetValue(keys[i], out var existing))
            {
                table = existing as TomlTable
                    ?? throw new InvalidOperationException(
                        $"'{file}' holds a value at '{string.Join('.', keys.Take(i + 1))}' where the listing of {loader.Name} expects a table.");
            }
            else
            {
                var created = new TomlTable();
                table[keys[i]] = created;
                table = created;
            }
        }

        table[keys[^1]] = value;
        return TomlSerializer.Serialize(root, options);
    }

    /// <summary>
    /// The parser merges dotted keys and repeated headers into one table, and
    /// each table array member is a table of its own.
    /// </summary>
    private static string? DuplicateKey(string text, TomlSerializerOptions options)
    {
        var parser = TomlParser.Create(text, options);
        var scopes = new Stack<HashSet<string>>();
        while (parser.MoveNext())
        {
            switch (parser.Current.Kind)
            {
                case TomlParseEventKind.StartTable:
                    scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case TomlParseEventKind.EndTable:
                    scopes.Pop();
                    break;
                case TomlParseEventKind.PropertyName:
                    var name = parser.GetPropertyName();
                    if (!scopes.Peek().Add(name))
                        return name;
                    break;
            }
        }

        return null;
    }

    private static string Absolute(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A path is required.", paramName);

        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("The path must be absolute, because the loader resolves it in its own working directory.", paramName);

        return Path.GetFullPath(path);
    }
}
