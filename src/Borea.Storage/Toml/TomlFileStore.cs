using Tomlyn;

namespace Borea.Storage.Toml;

/// <summary>
/// Generic helper for reading and writing a single object as a TOML file.
/// Wraps Tomlyn so every concrete repository shares the same file I/O and
/// directory-creation behavior instead of reimplementing it per DTO type.
/// </summary>
public static class TomlFileStore
{
    /// <summary>
    /// Reads and deserializes the TOML file at <paramref name="path"/> into
    /// <typeparamref name="T"/>, or returns null if the file does not exist.
    /// </summary>
    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken = default)
        where T : class
    {
        if (!File.Exists(path))
            return null;

        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return TomlSerializer.Deserialize<T>(text);
    }

    /// <summary>
    /// Serializes <paramref name="value"/> to TOML and writes it to
    /// <paramref name="path"/>, creating the containing directory if needed.
    /// Overwrites any existing file.
    /// </summary>
    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken = default)
        where T : class
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var text = TomlSerializer.Serialize(value);
        await File.WriteAllTextAsync(path, text, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the TOML file at <paramref name="path"/> if present. No-op if
    /// it doesn't exist.
    /// </summary>
    public static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}