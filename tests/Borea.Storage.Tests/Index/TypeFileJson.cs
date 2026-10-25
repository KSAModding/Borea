namespace Borea.Storage.Tests.Index;

/// <summary>Entries of the vehicle and save files in the shape the index stamps them (RFC 0085).</summary>
internal static class TypeFileJson
{
    public const string DerivedFolder = """{ "derived": true, "folder": "Example Rover" }""";

    /// <summary>No compatibility table and no links, which only a vehicle or save may leave out.</summary>
    public static string Authored(string id = "ExampleRover", string type = "vehicle") => $$"""
        {
            "spec_version": 1,
            "id": "{{id}}",
            "type": "{{type}}",
            "name": "Example Rover",
            "authors": ["Kitten"],
            "abstract": "A small rover.",
            "license": "CC0-1.0"
        }
        """;

    public static string Release(string id = "ExampleRover", string type = "vehicle", string install = DerivedFolder, string version = "1.0.0") => $$"""
        {
            "spec_version": 1,
            "id": "{{id}}",
            "type": "{{type}}",
            "version": "{{version}}",
            "version_scheme": "semver",
            "release_status": "stable",
            "release_date": "2026-10-20T12:00:00Z",
            "game_min": "2026.10.19.5638",
            "game_min_revision": 5638,
            "download": { "url": "https://example.test/rover.zip", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "size": 1024, "content_type": "application/zip" },
            "install": {{install}},
            "install_size": 2048,
            "dependencies": [],
            "listing": { "name": "Example Rover", "authors": ["Kitten"], "abstract": "A small rover.", "license": "CC0-1.0", "tags": [], "links": {} }
        }
        """;

    public static string Entry(string id = "ExampleRover", string type = "vehicle", string? releases = null, string extra = "") =>
        $$"""{ "id": "{{id}}", "authored": {{Authored(id, type)}}, "releases": [{{releases ?? Release(id, type)}}]{{extra}} }""";

    public static string File(string arrayKey, string entries, int snapshotVersion = 1) =>
        $$"""{ "snapshot_version": {{snapshotVersion}}, "{{arrayKey}}": [{{entries}}] }""";
}
