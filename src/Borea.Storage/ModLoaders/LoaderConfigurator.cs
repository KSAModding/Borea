using System.Text.Json;
using System.Text.Json.Nodes;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Files;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Serialization;

namespace Borea.Storage.ModLoaders;

/// <summary>
/// The file is read as a tree, the one key is set, and the tree is written
/// back, so every other key survives.
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
        var game = Path.TrimEndingDirectorySeparator(Absolute(gameDirectory, nameof(gameDirectory)));

        var configure = loader.Provides?.Configure;
        if (configure?.GamePath is null)
            return null;

        if (configure.Format is not (ConfigureFormat.Json or ConfigureFormat.Toml))
            throw new NotSupportedException($"The listing of {loader.Name} keeps its configuration in a format this version of Borea cannot write.");

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

    public async Task<string?> ReadConfiguredGamePathAsync(
        ModMetadata loader,
        string loaderDirectory,
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

        var file = Path.GetFullPath(Path.Combine(directory, configure.File.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(file))
            return null;

        var text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var keys = configure.GamePath.Split('.');
        return configure.Format == ConfigureFormat.Json
            ? ReadJsonGamePath(text, keys, file, loader)
            : ReadTomlGamePath(text, keys, file, loader);
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
