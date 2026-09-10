using System.Text.Json;

namespace Borea.Storage.Tests.Index;

internal static class IndexJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        RespectNullableAnnotations = true
    };

    public static T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, Options)
           ?? throw new JsonException($"The JSON document did not contain a {typeof(T).Name} value.");

    public static T Deserialize<T>(JsonElement element)
        => element.Deserialize<T>(Options)
           ?? throw new JsonException($"The JSON element did not contain a {typeof(T).Name} value.");
}
