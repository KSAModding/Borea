using System.Text.Json.Serialization;

namespace Borea.Storage.Index;

public sealed class InstallInfoDto
{
    [JsonPropertyName("root")]
    public required string Root { get; set; }

    [JsonPropertyName("derived")]
    public required bool Derived { get; set; }
}
