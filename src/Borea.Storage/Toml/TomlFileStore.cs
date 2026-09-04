using System.Globalization;
using Tomlyn;
using Tomlyn.Serialization;

namespace Borea.Storage.Toml;

/// <summary>
/// Generic helper for reading and writing a single object as a TOML file.
/// Wraps Tomlyn so every concrete repository shares the same file I/O and
/// directory-creation behavior instead of reimplementing it per DTO type.
/// </summary>
public static class TomlFileStore
{
    private static readonly TomlSerializerOptions Options = new()
    {
        Converters = new TomlConverter[] { RoundTripDateTimeOffsetConverter.Instance },
    };

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
        return TomlSerializer.Deserialize<T>(text, Options);
    }

    /// <summary>
    /// Serializes <paramref name="value"/> to TOML and writes it to
    /// <paramref name="path"/>, creating the containing directory if needed.
    /// Replaces any existing file, and leaves it untouched when the write does
    /// not finish.
    /// </summary>
    /// <remarks>Not safe against a second writer of the same path.</remarks>
    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken = default)
        where T : class
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var text = TomlSerializer.Serialize(value, Options);

        // Written beside the file and moved onto it, because writing in place
        // truncates first and the game parses manifest.toml with no error
        // handling. Same directory, so the move is atomic. The name is unique per
        // write, so a second writer cannot delete this one's temporary file.
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
    /// Clears the temporary file when the write did not reach the move, without
    /// replacing the error that caused it.
    /// </summary>
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

    /// <summary>
    /// Deletes the TOML file at <paramref name="path"/> if present. No-op if
    /// it doesn't exist.
    /// </summary>
    public static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>
    /// Persists DateTimeOffset as an ISO 8601 round-trip string, because the
    /// default TOML datetime form drops the fractional seconds. Reads both the
    /// string form and a plain TOML datetime.
    /// </summary>
    private sealed class RoundTripDateTimeOffsetConverter : TomlConverter<DateTimeOffset>
    {
        public static RoundTripDateTimeOffsetConverter Instance { get; } = new();

        public override DateTimeOffset Read(TomlReader reader)
        {
            if (reader.TokenType == TomlTokenType.String)
            {
                var text = reader.GetString();
                reader.Read();
                return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            }

            if (reader.TokenType == TomlTokenType.DateTime)
            {
                var value = reader.GetTomlDateTime();
                reader.Read();
                return value.DateTime;
            }

            throw reader.CreateException($"Expected a string or datetime token but was {reader.TokenType}.");
        }

        public override void Write(TomlWriter writer, DateTimeOffset value)
            => writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
    }
}
