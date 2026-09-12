using System.Text.Json;

namespace Borea.Storage.Index;

/// <summary>
/// The <see cref="JsonSerializerOptions"/> every index parser deserializes
/// with, kept in one place so the whole pipeline stays in sync.
/// </summary>
public static class IndexJsonOptions
{
    public static JsonSerializerOptions Value { get; } = new()
    {
        RespectNullableAnnotations = true,
    };
}
