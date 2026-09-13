using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Network.Index;
using Borea.Storage.Index;

namespace Borea.Cli.Tests;

public sealed class ShowCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task Show_PrintsTheListingLinksStatusAndEveryReleaseNewestFirst()
    {
        var listing = ContentCommandFixtures.Listing(
            status: ModStatus.Deprecated,
            supersededBy: "new-flight-tools");
        var newest = ContentCommandFixtures.Release(version: "2.0.0");
        var yanked = ContentCommandFixtures.Release(
            version: "1.0.0",
            yanked: true,
            yankedReason: "This release is broken.");
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.AddRange(new[] { yanked, newest });
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(
            listing.ModId,
            listing,
            new[] { yanked, newest },
            new IndexStatus(IndexStatusState.Disputed, "disputed", reason: "Ownership is under review.")));
        _host.InstalledVersion = Installed("2026.9.7.5402");

        var run = await _host.RunAsync("show", "flight-tools");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Flight Tools (flight-tools)", run.Output);
        Assert.Contains("Status: deprecated", run.Output);
        Assert.Contains("Superseded by: new-flight-tools", run.Output);
        Assert.Contains("repository: https://example.com/flight-tools", run.Output);
        Assert.Contains("Index status: disputed", run.Output);
        Assert.True(run.Output.IndexOf("2.0.0", StringComparison.Ordinal) < run.Output.IndexOf("1.0.0", StringComparison.Ordinal));
        Assert.Contains("1.0.0  compatible  stable  yanked: This release is broken.", run.Output);
    }

    [Fact]
    public async Task ShowVersion_PrintsOneReleaseAndItsDependencies()
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release(dependencies: new ModDependency[]
        {
            new("required-lib", ModDependencyKind.Required, ModVersion.Parse("1.0.0"), source: MetadataSource.Authored),
            ModDependency.OfAlternatives(ModDependencyKind.Recommends, new[]
            {
                new ModDependencyAlternative("first-lib", ModVersion.Parse("2.0.0")),
                new ModDependencyAlternative("second-lib"),
            }, MetadataSource.Derived),
        });
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(release);
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(listing.ModId, listing, new[] { release }, null));

        var run = await _host.RunAsync("show", "flight-tools", "--version", "2.0.0");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("required  required-lib >= 1.0.0 (authored)", run.Output);
        Assert.Contains("recommends  any of [first-lib >= 2.0.0, second-lib] (derived)", run.Output);
        Assert.Contains((listing.ModId, ModVersion.Parse("2.0.0")), _host.Mods.ReleaseRequests);
    }

    [Fact]
    public async Task Show_UnknownReleaseSpecVersion_KeepsItInOrderInJson()
    {
        var listing = ContentCommandFixtures.Listing();
        var known = ContentCommandFixtures.Release(version: "2.0.0");
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(known);
        _host.IndexReader.Snapshot = Snapshot(
            new[] { new ContentIndexListing(listing.ModId, listing, new[] { known }, null) },
            new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.UnsupportedVersion,
                    ContentIndexDiagnosticScope.Release,
                    "Release spec version 2 is newer than this client.",
                    listing.ModId,
                    "3.0.0",
                    2),
            });

        var run = await _host.RunAsync("show", listing.ModId, "--json");

        Assert.Equal(0, run.ExitCode);
        var releases = run.Json.GetProperty("releases");
        Assert.Equal(2, releases.GetArrayLength());
        Assert.Equal("3.0.0", releases[0].GetProperty("version").GetString());
        Assert.Equal("unknown", releases[0].GetProperty("state").GetString());
        Assert.Equal(2, releases[0].GetProperty("specVersion").GetInt32());
        Assert.Equal("2.0.0", releases[1].GetProperty("version").GetString());
        Assert.Equal("known", releases[1].GetProperty("state").GetString());
    }

    [Fact]
    public async Task Show_UnknownSpecVersion_KeepsTheListingIdAndDiagnostic()
    {
        _host.IndexReader.Snapshot = Snapshot(
            listings: Array.Empty<ContentIndexListing>(),
            diagnostics: new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.UnsupportedVersion,
                    ContentIndexDiagnosticScope.Listing,
                    "Listing spec version 2 is newer than this client.",
                    "future-tools",
                    SpecVersion: 2),
            });

        var run = await _host.RunAsync("show", "future-tools");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("future-tools (unknown)", run.Output);
        Assert.Contains("unsupported-version listing future-tools (spec version 2)", run.Output);
    }

    [Fact]
    public async Task Show_UnknownIndexStatus_KeepsRawStatusAndDiagnostic()
    {
        var listing = ContentCommandFixtures.Listing();
        var status = new IndexStatus(IndexStatusState.Unknown, "future-state", reason: "A future warning.");
        _host.Mods.Listings.Add(listing);
        _host.IndexReader.Snapshot = Snapshot(
            new[] { new ContentIndexListing(listing.ModId, listing, Array.Empty<ModVersionMetadata>(), status) },
            new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.UnsupportedValue,
                    ContentIndexDiagnosticScope.IndexStatus,
                    "Index status state 'future-state' is not supported.",
                    listing.ModId),
            });

        var run = await _host.RunAsync("show", listing.ModId);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Index status: future-state", run.Output);
        Assert.Contains("unsupported-value index-status flight-tools", run.Output);
    }

    [Fact]
    public async Task Show_DelistedTombstone_PrintsTheIndexStatus()
    {
        var tombstone = new ContentIndexListing(
            "removed-tools",
            null,
            Array.Empty<ModVersionMetadata>(),
            new IndexStatus(IndexStatusState.Delisted, "delisted", reason: "Removed by a steward."));
        _host.IndexReader.Snapshot = Snapshot(tombstone);

        var run = await _host.RunAsync("show", "removed-tools");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("removed-tools (delisted)", run.Output);
        Assert.Contains("Index status: delisted", run.Output);
        Assert.Contains("Index reason: Removed by a steward.", run.Output);
    }

    [Fact]
    public async Task ShowVersion_UnknownReleaseSpecVersion_PrintsUnknownAndTheDiagnostic()
    {
        var listing = ContentCommandFixtures.Listing();
        _host.Mods.Listings.Add(listing);
        _host.IndexReader.Snapshot = Snapshot(
            new[] { new ContentIndexListing(listing.ModId, listing, Array.Empty<ModVersionMetadata>(), null) },
            new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.UnsupportedVersion,
                    ContentIndexDiagnosticScope.Release,
                    "Release spec version 2 is newer than this client.",
                    listing.ModId,
                    "3.0.0",
                    2),
            });

        var run = await _host.RunAsync("show", listing.ModId, "--version", "3.0.0+local.1");
        var json = await _host.RunAsync("show", listing.ModId, "--version", "3.0.0+local.1", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("3.0.0  unknown (spec version 2)", run.Output);
        Assert.Contains("unsupported-version release flight-tools 3.0.0", run.Output);
        var unknown = Assert.Single(json.Json.GetProperty("releases").EnumerateArray());
        Assert.Equal("unknown", unknown.GetProperty("state").GetString());
        Assert.Equal("3.0.0", unknown.GetProperty("version").GetString());
        Assert.Equal(2, unknown.GetProperty("specVersion").GetInt32());
    }

    [Fact]
    public async Task Show_DesignSnapshotFixture_UsesMappedReleaseDataWithoutNetwork()
    {
        var indexPath = _host.Paths.GetIndexPath();
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Index", "Fixtures", "current-snapshot.json"),
            indexPath);
        var snapshot = await new ContentIndexReader(_host.Paths).ReadAsync();
        _host.IndexReader.Snapshot = snapshot;
        foreach (var entry in snapshot.Listings.Where(entry => entry.Authored is not null))
        {
            _host.Mods.Listings.Add(entry.Authored!);
            _host.Mods.Releases.AddRange(entry.Releases);
        }
        _host.InstalledVersion = Installed("2026.9.7.5402");

        var run = await _host.RunAsync(
            "show",
            "AdvancedFlightComputer",
            "--version",
            "0.7.5",
            "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Advanced Flight Computer", run.Json.GetProperty("listing").GetProperty("name").GetString());
        var release = Assert.Single(run.Json.GetProperty("releases").EnumerateArray());
        Assert.Equal("0.7.5", release.GetProperty("version").GetString());
        Assert.Equal("compatible", release.GetProperty("compatibility").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Array, release.GetProperty("dependencies").ValueKind);
        Assert.Null(_host.IndexFetcher.DestinationPath);
    }

    [Fact]
    public async Task Show_WarmedSnapshot_RemainsUsableWhenTheReaderFails()
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release();
        _host.IndexReader.Snapshot = Snapshot(
            new[]
            {
                new ContentIndexListing(
                    listing.ModId,
                    listing,
                    new[] { release },
                    new IndexStatus(IndexStatusState.Disputed, "disputed", reason: "Ownership is under review.")),
            },
            new[]
            {
                new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.UnsupportedValue,
                    ContentIndexDiagnosticScope.IndexStatus,
                    "Index status state 'future-state' is not supported.",
                    listing.ModId),
            });
        var snapshots = new ContentIndexSnapshotProvider(_host.IndexFetcher, _host.IndexReader, _host.Paths);
        _host.IndexSnapshots = snapshots;
        _host.ModRepository = new ContentIndexModRepository(snapshots);
        Assert.Single(await _host.ModRepository.GetAvailableModsAsync());
        _host.IndexReader.Read = _ => throw new IOException("The cache file is temporarily unreadable.");

        var run = await _host.RunAsync("show", listing.ModId);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Flight Tools (flight-tools)", run.Output);
        Assert.Contains("Index status: disputed", run.Output);
        Assert.Contains("unsupported-value index-status flight-tools", run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task ShowVersion_Json_CarriesCompatibilityYankAndDependencies()
    {
        var listing = ContentCommandFixtures.Listing();
        var release = ContentCommandFixtures.Release(
            gameMaxRevision: 5348,
            yanked: true,
            yankedReason: "Broken on newer games.",
            dependencies: new[]
            {
                new ModDependency("required-lib", ModDependencyKind.Required, ModVersion.Parse("1.0.0")),
            });
        _host.Mods.Listings.Add(listing);
        _host.Mods.Releases.Add(release);
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexListing(listing.ModId, listing, new[] { release }, null));
        _host.InstalledVersion = Installed("2026.9.7.5402");

        var run = await _host.RunAsync("show", listing.ModId, "--version", "2.0.0", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("known", run.Json.GetProperty("state").GetString());
        Assert.Equal("Flight Tools", run.Json.GetProperty("listing").GetProperty("name").GetString());
        var shown = run.Json.GetProperty("releases")[0];
        Assert.Equal("untested", shown.GetProperty("compatibility").GetString());
        Assert.True(shown.GetProperty("yanked").GetBoolean());
        Assert.Equal("Broken on newer games.", shown.GetProperty("yankedReason").GetString());
        Assert.Equal("required", shown.GetProperty("dependencies")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Show_MissingListing_Fails()
    {
        var run = await _host.RunAsync("show", "missing-tools");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Listing 'missing-tools' was not found.", run.Error);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("")]
    public async Task ShowVersion_InvalidVersion_IsAUsageError(string version)
    {
        var run = await _host.RunAsync("show", "flight-tools", "--version", version);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(0, _host.Builds);
    }

    private static FakeInstalledGameVersionProvider Installed(string version) => new()
    {
        Installed = new InstalledGameVersion(GameVersion.Parse(version), version),
    };

    private static ContentIndexSnapshot Snapshot(params ContentIndexListing[] listings) =>
        Snapshot(listings, Array.Empty<ContentIndexDiagnostic>());

    private static ContentIndexSnapshot Snapshot(
        IReadOnlyList<ContentIndexListing> listings,
        IReadOnlyList<ContentIndexDiagnostic> diagnostics) => new(
            1,
            listings,
            Array.Empty<ContentIndexPack>(),
            null,
            diagnostics);

    public void Dispose() => _host.Dispose();
}
