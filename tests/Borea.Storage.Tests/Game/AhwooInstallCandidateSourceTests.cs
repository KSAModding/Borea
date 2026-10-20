using Borea.Storage.Game;

namespace Borea.Storage.Tests.Game;

public sealed class AhwooInstallCandidateSourceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    private string Config => Path.Combine(_tempRoot, "config", "Ahwoo");

    private string Games => Path.Combine(_tempRoot, "Games", "Ahwoo");

    private AhwooInstallCandidateSource Source() => new(Config, Games);

    private void PlaceManifest(string id, string json)
    {
        Directory.CreateDirectory(Path.Combine(Config, "Manifests"));
        File.WriteAllText(Path.Combine(Config, "Manifests", id + ".adf"), json);
    }

    private static string Manifest(string idField, string id, string installDirectory) =>
        $$"""{ "SchemaVersion": 2, "{{idField}}": "{{id}}", "Builds": [ { "InstallDirectory": "{{installDirectory}}" } ] }""";

    [Theory]
    [InlineData("ApplicationId")]
    [InlineData("PackageId")]
    public void GetGameDirectories_ReadsTheGamePackageOnly(string idField)
    {
        PlaceManifest("019fab3e-7ac8-7228-ba27-aca60c741e91", Manifest(idField, "019fab3e-7ac8-7228-ba27-aca60c741e91", "Gruntz"));
        PlaceManifest("019f01fc-623a-773f-8d70-f0177b16a20c", Manifest(idField, "019f01fc-623a-773f-8d70-f0177b16a20c", "Kitten Space Agency"));
        PlaceManifest("00000000-0000-7000-8000-000000000000", Manifest(idField, "00000000-0000-7000-8000-000000000000", "KSA Server"));

        Assert.Equal([Path.Combine(Games, "Kitten Space Agency")], Source().GetGameDirectories());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ \"Builds\": [")]
    [InlineData("{ \"Builds\": 2 }")]
    public void GetGameDirectories_MissingOrBrokenManifest_ReturnsNone(string? json)
    {
        if (json is not null)
            PlaceManifest("019f01fc-623a-773f-8d70-f0177b16a20c", json);

        Assert.Empty(Source().GetGameDirectories());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
