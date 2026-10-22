using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Borea.Core.Index;

namespace Borea.App.Tests.ViewModels;

/// <summary>Vehicle and save entries of the index type files, and their archives (RFC 0085).</summary>
internal static class UserContentFixtures
{
    public const string RoverUrl = "https://example.test/rover.zip";
    public const string SaveUrl = "https://example.test/campaign.zip";

    public static byte[] RoverArchive() =>
        Archive(("Example Rover/meta.toml", "name = \"Example Rover\"\n"), ("Example Rover/vehicle.xml", "<Vehicle />"));

    public static Dictionary<string, string> TypeFiles(ContentIndexTypeFile file, string entries) =>
        new() { [file.FileName] = $$"""{ "snapshot_version": 1, "{{file.ArrayKey}}": [{{entries}}] }""" };

    public static string Entry(string id, string type, string folder, string url, byte[] archive, params string[] versions) =>
        EntryNeeding(id, type, folder, url, archive, "", versions);

    /// <param name="dependencies">The entries of the release's dependencies array, as JSON.</param>
    public static string EntryNeeding(string id, string type, string folder, string url, byte[] archive, string dependencies, IReadOnlyList<string> versions) =>
        $$"""{ "id": "{{id}}", "authored": {{Authored(id, type, folder)}}, "releases": [{{string.Join(", ", versions.Select(version => Release(id, type, folder, url, archive, version, dependencies)))}}] }""";

    public static string SaveEntry(params (string Version, byte[] Archive)[] releases) =>
        $$"""{ "id": "CampaignSave", "authored": {{Authored("CampaignSave", "save", "Campaign")}}, "releases": [{{string.Join(", ", releases.Select(release => Release("CampaignSave", "save", "Campaign", SaveUrl, release.Archive, release.Version, "")))}}] }""";

    /// <summary>A required entry of a release's dependencies array.</summary>
    public static string Required(string id, string? min = null, string? max = null) =>
        "{ \"id\": \"" + id + "\", \"kind\": \"required\"" + (min is null ? "" : ", \"min\": \"" + min + "\"") + (max is null ? "" : ", \"max\": \"" + max + "\"") + ", \"source\": \"authored\" }";

    private static string Authored(string id, string type, string folder) =>
        $$"""{ "spec_version": 1, "id": "{{id}}", "type": "{{type}}", "name": "{{folder}}", "authors": ["Kitten"], "abstract": "A {{type}}.", "license": "CC0-1.0" }""";

    private static string Release(string id, string type, string folder, string url, byte[] archive, string version, string dependencies) => $$"""
        { "spec_version": 1, "id": "{{id}}", "type": "{{type}}", "version": "{{version}}", "version_scheme": "semver", "release_status": "stable",
          "release_date": "2026-10-20T12:00:00Z", "game_min": "2026.8.19.5261", "game_min_revision": 5261,
          "download": { "url": "{{url}}", "sha256": "{{Convert.ToHexString(SHA256.HashData(archive))}}", "size": {{archive.Length}}, "content_type": "application/zip" },
          "install": { "derived": true, "folder": "{{folder}}" }, "install_size": 2048, "dependencies": [{{dependencies}}],
          "listing": { "name": "{{folder}}", "authors": ["Kitten"], "abstract": "A {{type}}.", "license": "CC0-1.0", "tags": [], "links": {} } }
        """;

    public static byte[] SaveArchive(string universe) =>
        Archive(("Campaign/meta.toml", "name = \"Campaign\"\n"), ("Campaign/universe.xml", universe));

    public static byte[] Archive(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    public static HttpResponseMessage ArchiveResponse(byte[] archive)
    {
        var content = new ByteArrayContent(archive);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
