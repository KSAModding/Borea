using Borea.Core.Dependencies;
using System.Text.Json.Serialization;
using Tomlyn;

namespace Borea.Network;

/// <summary>
/// Reads a downloaded mod's own mod.toml to confirm its true, permanent
/// ModId (the "name" field — KSA/StarMap have no separate ID field; the
/// mod's name IS its identity). Source-agnostic: any IModDownloader
/// implementation can use this once files are on disk, regardless of
/// which backend produced them.
/// </summary>
internal static class ModTomlReader
{
    private const string FileName = "mod.toml";

    private sealed class ModTomlDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        public List<ModDependencyDto> ModDependencies { get; set; } = new();
    }

    private sealed class ModDependencyDto
    {
        public string ModId { get; set; } = String.Empty;
        public bool Optional { get; set; } = false;
    }

    public static string ReadModId(string modDirectory)
    {
        var path = Path.Combine(modDirectory, FileName);

        if (!File.Exists(path))
            throw new InvalidOperationException($"'{FileName}' not found in '{modDirectory}'.");

        var text = File.ReadAllText(path);
        var dto = TomlSerializer.Deserialize<ModTomlDto>(text)
            ?? throw new InvalidOperationException($"Failed to parse '{path}'.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException($"'{FileName}' at '{path}' is missing the required 'name' field.");

        return dto.Name;
    }
}