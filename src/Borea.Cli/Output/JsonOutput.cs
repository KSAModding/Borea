using System.Text.Json;

namespace Borea.Cli.Output;

/// <summary>
/// Writes a command's result as JSON: camelCase keys, dictionary keys as they
/// are, indented.
/// </summary>
internal static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void Write<T>(TextWriter output, T value)
        => output.WriteLine(JsonSerializer.Serialize(value, Options));
}
