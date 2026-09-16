using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Storage.Instances;

namespace Borea.Cli.Tests;

public sealed class PackCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task PackSearch_Hit_PrintsIdNameNewestUsableVersionAndCompatibility()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(
            Usable(ContentCommandFixtures.PackVersion(version: "1.0.0")),
            Usable(ContentCommandFixtures.PackVersion(version: "2.0.0")),
            ContentCommandFixtures.Retracted(ContentCommandFixtures.PackVersion(version: "3.0.0"))));
        _host.InstalledVersion = Installed("2026.9.7.5402");

        var run = await _host.RunAsync("pack", "search", "Navigation");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("navigation-pack  Navigation Pack  2.0.0  compatible", run.Output);
        Assert.DoesNotContain("3.0.0", run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task PackSearch_NoHit_PrintsAnEmptyResult()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.PackVersion()));

        var run = await _host.RunAsync("pack", "search", "engines");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("No packs match 'engines'.", run.Output);
    }

    [Fact]
    public async Task PackSearch_DelistedRetractedAndUnknownPacks_StayVisible()
    {
        _host.IndexReader.Snapshot = Snapshot(
            new[]
            {
                new ContentIndexPack("navigation-old", Array.Empty<ContentIndexPackVersion>(), new IndexStatus(IndexStatusState.Delisted, "delisted")),
                Pack(ContentCommandFixtures.Retracted(ContentCommandFixtures.PackVersion(id: "navigation-broken", name: "Broken Navigation"))),
            },
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedVersion,
                ContentIndexDiagnosticScope.Pack,
                "Pack spec version 2 is newer than this client.",
                "navigation-future",
                SpecVersion: 2));

        var run = await _host.RunAsync("pack", "search", "navigation");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("navigation-broken  Broken Navigation  no usable version  unknown", run.Output);
        Assert.Contains("navigation-future  unknown pack  unknown  unknown", run.Output);
        Assert.Contains("navigation-old  delisted pack  unknown  unknown", run.Output);
        Assert.Contains("unsupported-version pack navigation-future (spec version 2)", run.Output);
    }

    [Fact]
    public async Task PackSearch_MonthBound_ResolvesThroughTheGameReleaseList()
    {
        _host.IndexReader.Snapshot = WithGameVersions(
            Snapshot(Pack(ContentCommandFixtures.PackVersion(gameMin: "2026.9"))),
            "2026.8.22.5348",
            "2026.9.7.5402");
        _host.InstalledVersion = Installed("2026.8.22.5348");

        var run = await _host.RunAsync("pack", "search", "Navigation");

        Assert.Contains("navigation-pack  Navigation Pack  1.0.0  incompatible", run.Output);
    }

    [Fact]
    public async Task PackSearch_MonthTheListDoesNotKnow_PrintsUnknownCompatibility()
    {
        _host.IndexReader.Snapshot = WithGameVersions(
            Snapshot(Pack(ContentCommandFixtures.PackVersion(gameMin: "2026.10"))),
            "2026.9.7.5402");
        _host.InstalledVersion = Installed("2026.9.7.5402");

        var run = await _host.RunAsync("pack", "search", "Navigation");

        Assert.Contains("navigation-pack  Navigation Pack  1.0.0  unknown", run.Output);
    }

    [Fact]
    public async Task PackSearch_MonthUpperBound_KeepsAnIncompatibleLowerBound()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.PackVersion(gameMin: "2026.9.7.5402", gameMax: "2026.9")));
        _host.InstalledVersion = Installed("2026.8.22.5348");

        var run = await _host.RunAsync("pack", "search", "Navigation");

        Assert.Contains("navigation-pack  Navigation Pack  1.0.0  incompatible", run.Output);
    }

    [Fact]
    public async Task PackSearch_Json_CarriesKnownAndUnknownResultsAndDiagnostics()
    {
        _host.IndexReader.Snapshot = Snapshot(
            new[] { Pack(ContentCommandFixtures.PackVersion(gameMin: "2026.9.7.5402")) },
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedVersion,
                ContentIndexDiagnosticScope.Pack,
                "Pack spec version 2 is newer than this client.",
                "navigation-future",
                SpecVersion: 2));
        _host.InstalledVersion = Installed("2026.8.22.5348");

        var run = await _host.RunAsync("pack", "search", "navigation", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("2026.8.22.5348", run.Json.GetProperty("installedGameVersion").GetString());
        var results = run.Json.GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());
        Assert.Equal("navigation-future", results[0].GetProperty("id").GetString());
        Assert.Equal("unknown", results[0].GetProperty("state").GetString());
        Assert.Equal("navigation-pack", results[1].GetProperty("id").GetString());
        Assert.Equal("1.0.0", results[1].GetProperty("latestVersion").GetString());
        Assert.Equal("incompatible", results[1].GetProperty("compatibility").GetString());
        Assert.Equal("pack", Assert.Single(run.Json.GetProperty("diagnostics").EnumerateArray()).GetProperty("scope").GetString());
    }

    [Fact]
    public async Task PackShow_PrintsThePackLinksStatusAndEveryVersionNewestFirst()
    {
        _host.IndexReader.Snapshot = Snapshot(
            new[]
            {
                new ContentIndexPack(
                    "navigation-pack",
                    new[]
                    {
                        new ContentIndexPackVersion(ContentCommandFixtures.PackVersion(version: "1.0.0"), null),
                        ContentCommandFixtures.Retracted(ContentCommandFixtures.PackVersion(version: "2.0.0")),
                        new ContentIndexPackVersion(ContentCommandFixtures.PackVersion(version: "3.0.0", status: ModStatus.Deprecated, supersededBy: "route-pack"), null),
                    },
                    new IndexStatus(IndexStatusState.Disputed, "disputed", reason: "Ownership is under review.")),
            },
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedVersion,
                ContentIndexDiagnosticScope.PackVersion,
                "Pack version spec version 2 is newer than this client.",
                "navigation-pack",
                "4.0.0",
                2));
        _host.InstalledVersion = Installed("2026.9.7.5402");

        var run = await _host.RunAsync("pack", "show", "navigation-pack");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Navigation Pack (navigation-pack)", run.Output);
        Assert.Contains("Status: deprecated", run.Output);
        Assert.Contains("Superseded by: route-pack", run.Output);
        Assert.Contains("forums: https://forums.example/threads/navigation-pack.2/", run.Output);
        Assert.Contains("Index status: disputed", run.Output);
        Assert.Contains("Index reason: Ownership is under review.", run.Output);
        Assert.Contains("  2.0.0  compatible  released 2026-09-02  retracted: This pack version is broken.", run.Output);
        var order = new[] { "  4.0.0  unknown (spec version 2)", "  3.0.0  compatible", "  2.0.0  compatible", "  1.0.0  compatible" }
            .Select(line => run.Output.IndexOf(line, StringComparison.Ordinal))
            .ToArray();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order(), order);
        Assert.DoesNotContain("Mods:", run.Output);
    }

    [Fact]
    public async Task PackShowVersion_PrintsPinnedModsVehiclesAndSaves()
    {
        var version = ContentCommandFixtures.PackVersion(
            mods: new[]
            {
                new ModPackEntry("flight-tools", ModVersion.Parse("2.0.0")),
                new ModPackEntry("library", ModVersion.Parse("1.0.0")),
                new ModPackEntry("missing-mod", ModVersion.Parse("1.0.0")),
            },
            vehicles: new[] { new ModPackEntry("booster-demo", ModVersion.Parse("1.2.0")) });
        _host.IndexReader.Snapshot = Snapshot(Pack(version));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "library", version: "1.0.0", yanked: true, yankedReason: "Broken build."));

        var run = await _host.RunAsync("pack", "show", "navigation-pack", "--version", "1.0.0");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Version:", run.Output);
        Assert.Contains("    Changelog: https://example.com/navigation-pack/changelog", run.Output);
        Assert.Contains("      flight-tools 2.0.0  listed", run.Output);
        Assert.Contains("      library 1.0.0  yanked: Broken build.", run.Output);
        Assert.Contains("      missing-mod 1.0.0  unlisted", run.Output);
        Assert.Contains("    Vehicles:", run.Output);
        Assert.Contains("      booster-demo 1.2.0", run.Output);
        Assert.Contains("    Saves: none", run.Output);
    }

    [Fact]
    public async Task PackShowVersion_RetractedVersion_Json_CarriesTheMarkerAndMembers()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(
            Usable(ContentCommandFixtures.PackVersion(version: "1.0.0")),
            ContentCommandFixtures.Retracted(ContentCommandFixtures.PackVersion(version: "2.0.0"))));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());

        var run = await _host.RunAsync("pack", "show", "navigation-pack", "--version", "2.0.0", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("known", run.Json.GetProperty("state").GetString());
        Assert.Equal("2.0.0", run.Json.GetProperty("requestedVersion").GetString());
        var shown = Assert.Single(run.Json.GetProperty("versions").EnumerateArray());
        Assert.True(shown.GetProperty("retracted").GetBoolean());
        Assert.Equal("This pack version is broken.", shown.GetProperty("indexStatus").GetProperty("reason").GetString());
        var member = Assert.Single(shown.GetProperty("mods").EnumerateArray());
        Assert.Equal("flight-tools", member.GetProperty("id").GetString());
        Assert.Equal("listed", member.GetProperty("state").GetString());
    }

    [Fact]
    public async Task PackShow_UnknownPackId_Fails()
    {
        var run = await _host.RunAsync("pack", "show", "missing-pack");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("error: Pack 'missing-pack' was not found.", run.Error);
    }

    [Fact]
    public async Task PackShowVersion_MissingVersion_Fails()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.PackVersion()));

        var run = await _host.RunAsync("pack", "show", "navigation-pack", "--version", "9.0.0");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Pack 'navigation-pack' 9.0.0 was not found.", run.Error);
    }

    [Fact]
    public async Task PackShow_DelistedPack_PrintsTheIndexStatus()
    {
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexPack(
            "removed-pack",
            Array.Empty<ContentIndexPackVersion>(),
            new IndexStatus(IndexStatusState.Delisted, "delisted", reason: "Removed by a steward.")));

        var run = await _host.RunAsync("pack", "show", "removed-pack");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("removed-pack (delisted)", run.Output);
        Assert.Contains("Index status: delisted", run.Output);
        Assert.Contains("Index reason: Removed by a steward.", run.Output);
        Assert.Contains("Versions: none", run.Output);
    }

    [Fact]
    public async Task PackShow_UnknownSpecVersion_KeepsThePackIdAndDiagnostic()
    {
        _host.IndexReader.Snapshot = Snapshot(
            Array.Empty<ContentIndexPack>(),
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedVersion,
                ContentIndexDiagnosticScope.Pack,
                "Pack spec version 2 is newer than this client.",
                "future-pack",
                SpecVersion: 2));

        var run = await _host.RunAsync("pack", "show", "future-pack");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("future-pack (unknown)", run.Output);
        Assert.Contains("unsupported-version pack future-pack (spec version 2)", run.Output);
    }

    [Fact]
    public async Task PackInstall_NewestUsableVersion_PassesTheExactPackAndPrintsMemberResults()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(
            Usable(ContentCommandFixtures.PackVersion(version: "1.0.0")),
            ContentCommandFixtures.Retracted(ContentCommandFixtures.PackVersion(version: "2.0.0"))));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        await _host.RunAsync("instance", "create", "Alpha");
        var instance = Assert.Single(await new FileInstanceRepository(_host.Paths).GetAllAsync());

        var run = await _host.RunAsync("pack", "install", "navigation-pack", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Pack navigation-pack 1.0.0 into 'Alpha':", run.Output);
        Assert.Contains("  installed  flight-tools 2.0.0  modpack", run.Output);
        Assert.Equal(string.Empty, run.Error);
        var request = Assert.Single(_host.ModPackInstaller.Requests);
        Assert.Equal(instance.InstanceId, request.InstanceId);
        Assert.Equal("1.0.0", request.Pack.Version);
        Assert.Same(_host.Mods, request.Repository);
        Assert.False(request.ProceedWithRetractedPack);
        Assert.Null(request.ProceedWithYankedMembers);
    }

    [Fact]
    public async Task PackInstall_RetractedVersionWithoutTheOption_FailsAndNamesTheOption()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.Retracted(ContentCommandFixtures.PackVersion(version: "2.0.0"))));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "navigation-pack", "--version", "2.0.0", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("warning: Pack version 2.0.0 is retracted. This pack version is broken.", run.Output);
        Assert.Contains("  unresolved  flight-tools 2.0.0  modpack", run.Output);
        Assert.Contains("error: Pack 'navigation-pack' 2.0.0 is retracted. Pass --proceed-with-retracted to install it anyway.", run.Error);
        Assert.False(Assert.Single(_host.ModPackInstaller.Requests).ProceedWithRetractedPack);
    }

    [Fact]
    public async Task PackInstall_RetractedVersionWithTheOption_PassesTheChoice()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.Retracted(ContentCommandFixtures.PackVersion(version: "2.0.0"))));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "navigation-pack", "--version", "2.0.0", "--instance", "Alpha", "--proceed-with-retracted");

        Assert.Equal(0, run.ExitCode);
        var request = Assert.Single(_host.ModPackInstaller.Requests);
        Assert.True(request.ProceedWithRetractedPack);
        Assert.Equal(IndexStatusState.Retracted, request.Pack.VersionStatus!.State);
    }

    [Fact]
    public async Task PackInstall_EveryVersionRetracted_FailsAndNamesTheVersionOption()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.Retracted(ContentCommandFixtures.PackVersion(version: "2.0.0"))));
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "navigation-pack", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("error: Pack 'navigation-pack' has no usable version. Pass --version to select a retracted version.", run.Error);
        Assert.Empty(_host.ModPackInstaller.Requests);
    }

    [Fact]
    public async Task PackInstall_YankedMember_NeedsTheExplicitOption()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.PackVersion(mods: new[]
        {
            new ModPackEntry("flight-tools", ModVersion.Parse("2.0.0")),
            new ModPackEntry("library", ModVersion.Parse("1.0.0")),
        })));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "library", version: "1.0.0", yanked: true, yankedReason: "Broken build."));
        await _host.RunAsync("instance", "create", "Alpha");

        var refused = await _host.RunAsync("pack", "install", "navigation-pack", "--instance", "Alpha");
        var accepted = await _host.RunAsync("pack", "install", "navigation-pack", "--instance", "Alpha", "--proceed-with-yanked", "LIBRARY");

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("warning: The selected release of library is yanked. Broken build.", refused.Output);
        Assert.Contains("  unresolved  library 1.0.0  modpack: Caller confirmation is required for the yanked release.", refused.Output);
        Assert.Contains("  not-attempted  flight-tools 2.0.0  modpack: Another pack member needs caller action.", refused.Output);
        Assert.Contains("error: The pack pins a yanked release of library. Pass --proceed-with-yanked with each mod id to install it anyway.", refused.Error);
        Assert.Equal(0, accepted.ExitCode);
        Assert.Contains("  installed  library 1.0.0  modpack", accepted.Output);
        Assert.Contains("library", _host.ModPackInstaller.Requests[1].ProceedWithYankedMembers!);
    }

    [Fact]
    public async Task PackInstall_PlannerYankedWarningAfterTheChoice_NamesTheRealFailure()
    {
        var flightTools = new ModPackEntry("flight-tools", ModVersion.Parse("2.0.0"));
        var library = new ModPackEntry("library", ModVersion.Parse("1.0.0"));
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.PackVersion(mods: new[] { flightTools, library })));
        _host.ModPackInstaller.Result = request => new ModPackInstallResult(
            request.InstanceId,
            null,
            new[]
            {
                FakeModPackInstaller.Member(flightTools, ModPackMemberStatus.Failed, "The archive hash did not match."),
                FakeModPackInstaller.Member(library, ModPackMemberStatus.Installed),
            },
            new[] { new Borea.Core.Planning.PlanningMessage("library", "yanked", "Broken build.") },
            false);
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "navigation-pack", "--instance", "Alpha", "--proceed-with-yanked", "library");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("error: The pack was not installed completely, because 1 of 2 members did not install.", run.Error);
        Assert.DoesNotContain("--proceed-with-yanked", run.Error);
    }

    [Fact]
    public async Task PackInstall_UnlistedPin_IsUnresolvedAndTheOtherPinsAreNotAttempted()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.PackVersion(mods: new[]
        {
            new ModPackEntry("flight-tools", ModVersion.Parse("2.0.0")),
            new ModPackEntry("missing-mod", ModVersion.Parse("1.0.0")),
        })));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "navigation-pack", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("warning: missing-mod: The exact pinned release is not listed and cannot be installed by Borea.", run.Output);
        Assert.Contains("  unresolved  missing-mod 1.0.0  modpack: The exact pinned release is not listed.", run.Output);
        Assert.Contains("  not-attempted  flight-tools 2.0.0  modpack: Another pack member needs caller action.", run.Output);
        Assert.Contains("error: The pack was not installed completely, because 2 of 2 members did not install.", run.Error);
    }

    [Fact]
    public async Task PackInstall_DisputedAndDeprecatedPack_WarnsAndInstalls()
    {
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexPack(
            "navigation-pack",
            new[] { Usable(ContentCommandFixtures.PackVersion(status: ModStatus.Deprecated, supersededBy: "route-pack")) },
            new IndexStatus(IndexStatusState.Disputed, "disputed", reason: "Ownership is under review.")));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        _host.InstalledVersion = Installed("2026.9.7.5402");
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "navigation-pack", "--instance", "Alpha");
        var json = await _host.RunAsync("pack", "install", "navigation-pack", "--instance", "Alpha", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("warning: Pack 'navigation-pack' is disputed. Ownership is under review.", run.Output);
        Assert.Contains("warning: Pack 'navigation-pack' is deprecated and superseded by 'route-pack'.", run.Output);
        Assert.Contains("  installed  flight-tools 2.0.0  modpack", run.Output);
        Assert.DoesNotContain("compatibility", run.Output);
        Assert.Equal(0, json.ExitCode);
        var codes = json.Json.GetProperty("warnings").EnumerateArray().Select(warning => warning.GetProperty("code").GetString()).ToArray();
        Assert.Equal(new[] { "pack-disputed", "pack-deprecated" }, codes);
    }

    [Fact]
    public async Task PackInstall_UnknownVersionIndexState_WarnsAndInstalls()
    {
        _host.IndexReader.Snapshot = Snapshot(
            new[]
            {
                Pack(new ContentIndexPackVersion(
                    ContentCommandFixtures.PackVersion(),
                    new IndexStatus(IndexStatusState.Unknown, "quarantined", reason: "Under review."))),
            },
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedValue,
                ContentIndexDiagnosticScope.IndexStatus,
                "Index status state 'quarantined' is not supported.",
                "navigation-pack",
                "1.0.0"));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "navigation-pack", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("warning: Pack version 1.0.0 has the index status 'quarantined', which this version of Borea does not know. Under review.", run.Output);
        Assert.Contains("unsupported-value index-status navigation-pack 1.0.0", run.Output);
        Assert.Contains("warning: The compatibility of pack 'navigation-pack' 1.0.0 with the installed game is unknown.", run.Output);
    }

    [Fact]
    public async Task PackInstall_UntestedPack_WarnsAndInstalls()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.PackVersion(gameMax: "2026.8.22.5348")));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        _host.InstalledVersion = Installed("2026.9.7.5402");
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "navigation-pack", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("warning: Pack 'navigation-pack' 1.0.0 is untested with the installed game, because the game is newer than 2026.8.22.5348.", run.Output);
        Assert.Single(_host.ModPackInstaller.Requests);
    }

    [Fact]
    public async Task PackInstall_IncompatiblePack_FailsAndNamesTheBuildItNeeds()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.PackVersion(gameMin: "2026.9.7.5402", gameMax: "2026.9")));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        _host.InstalledVersion = Installed("2026.8.22.5348");
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "navigation-pack", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("error: Pack 'navigation-pack' 1.0.0 is incompatible with the installed game 2026.8.22.5348, because it needs game 2026.9.7.5402 or newer.", run.Error);
        Assert.Empty(_host.ModPackInstaller.Requests);
    }

    [Fact]
    public async Task PackInstall_RetractedVersionsAndANewerFormatVersion_NamesTheVersionOption()
    {
        _host.IndexReader.Snapshot = Snapshot(
            new[] { Pack(ContentCommandFixtures.Retracted(ContentCommandFixtures.PackVersion(version: "2.0.0"))) },
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedVersion,
                ContentIndexDiagnosticScope.PackVersion,
                "Pack version spec version 2 is newer than this client.",
                "navigation-pack",
                "3.0.0",
                2));
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "navigation-pack", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("error: Pack 'navigation-pack' has no usable version. Pass --version to select a retracted version.", run.Error);
    }

    [Fact]
    public async Task PackInstall_WithoutInstance_UsesTheActiveInstance()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.PackVersion()));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        await _host.RunAsync("instance", "create", "Alpha");
        await _host.RunAsync("instance", "create", "Beta");
        await _host.RunAsync("instance", "activate", "Beta");
        var beta = Assert.Single(await new FileInstanceRepository(_host.Paths).GetAllAsync(), instance => instance.Name == "Beta");

        var run = await _host.RunAsync("pack", "install", "navigation-pack");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Pack navigation-pack 1.0.0 into 'Beta':", run.Output);
        Assert.Equal(beta.InstanceId, Assert.Single(_host.ModPackInstaller.Requests).InstanceId);
    }

    [Fact]
    public async Task PackInstall_NoActiveInstance_Fails()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.PackVersion()));

        var run = await _host.RunAsync("pack", "install", "navigation-pack");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("error: No instance is active.", run.Error);
        Assert.Empty(_host.ModPackInstaller.Requests);
    }

    [Fact]
    public async Task PackInstall_UnknownPackId_FailsWithoutInstalling()
    {
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "missing-pack", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("error: Pack 'missing-pack' was not found.", run.Error);
        Assert.Empty(_host.ModPackInstaller.Requests);
    }

    [Fact]
    public async Task PackInstall_DelistedPack_FailsWithoutInstalling()
    {
        _host.IndexReader.Snapshot = Snapshot(new ContentIndexPack(
            "removed-pack",
            Array.Empty<ContentIndexPackVersion>(),
            new IndexStatus(IndexStatusState.Delisted, "delisted")));
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "removed-pack", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("error: Pack 'removed-pack' was delisted from the content index and cannot be installed.", run.Error);
        Assert.Empty(_host.ModPackInstaller.Requests);
    }

    [Fact]
    public async Task PackInstall_UnknownSpecVersion_FailsWithoutInstalling()
    {
        _host.IndexReader.Snapshot = Snapshot(
            Array.Empty<ContentIndexPack>(),
            new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedVersion,
                ContentIndexDiagnosticScope.Pack,
                "Pack spec version 2 is newer than this client.",
                "future-pack",
                SpecVersion: 2));
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "future-pack", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("error: Pack 'future-pack' uses a metadata format that this version of Borea cannot read.", run.Error);
        Assert.Empty(_host.ModPackInstaller.Requests);
    }

    [Fact]
    public async Task PackInstall_PartialMemberFailure_ExitsWithFailed()
    {
        var flightTools = new ModPackEntry("flight-tools", ModVersion.Parse("2.0.0"));
        var library = new ModPackEntry("library", ModVersion.Parse("1.0.0"));
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.PackVersion(mods: new[] { flightTools, library })));
        _host.ModPackInstaller.Result = request => new ModPackInstallResult(
            request.InstanceId,
            null,
            new[]
            {
                FakeModPackInstaller.Member(flightTools, ModPackMemberStatus.Installed),
                FakeModPackInstaller.Member(library, ModPackMemberStatus.Failed, "The archive hash did not match."),
            },
            Array.Empty<Borea.Core.Planning.PlanningMessage>(),
            false);
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "navigation-pack", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("  installed  flight-tools 2.0.0  modpack", run.Output);
        Assert.Contains("  failed  library 1.0.0  modpack: The archive hash did not match.", run.Output);
        Assert.Contains("error: The pack was not installed completely, because 1 of 2 members did not install.", run.Error);
    }

    [Fact]
    public async Task PackInstall_VehiclesAndSaves_WarnsThatTheyAreSkipped()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.PackVersion(
            vehicles: new[] { new ModPackEntry("booster-demo", ModVersion.Parse("1.2.0")) },
            saves: new[] { new ModPackEntry("apollo-save", ModVersion.Parse("1.0.0")) })));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("pack", "install", "navigation-pack", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("warning: Borea does not install pinned vehicles and saves yet, so it skipped booster-demo 1.2.0, apollo-save 1.0.0.", run.Output);
    }

    [Fact]
    public async Task PackInstall_Json_CarriesMembersWarningsAndSkippedContent()
    {
        _host.IndexReader.Snapshot = Snapshot(Pack(ContentCommandFixtures.Retracted(ContentCommandFixtures.PackVersion(
            version: "2.0.0",
            saves: new[] { new ModPackEntry("apollo-save", ModVersion.Parse("1.0.0")) }))));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        _host.InstalledVersion = Installed("2026.9.7.5402");
        await _host.RunAsync("instance", "create", "Alpha");

        var refused = await _host.RunAsync("pack", "install", "navigation-pack", "--version", "2.0.0", "--instance", "Alpha", "--json");
        var accepted = await _host.RunAsync("pack", "install", "navigation-pack", "--version", "2.0.0", "--instance", "Alpha", "--proceed-with-retracted", "--json");

        Assert.Equal(1, refused.ExitCode);
        Assert.False(refused.Json.GetProperty("complete").GetBoolean());
        Assert.Equal("retracted-pack", Assert.Single(refused.Json.GetProperty("warnings").EnumerateArray()).GetProperty("code").GetString());
        Assert.Contains("--proceed-with-retracted", refused.Error);
        Assert.Equal(0, accepted.ExitCode);
        Assert.True(accepted.Json.GetProperty("complete").GetBoolean());
        Assert.Equal("Alpha", accepted.Json.GetProperty("instanceName").GetString());
        var member = Assert.Single(accepted.Json.GetProperty("members").EnumerateArray());
        Assert.Equal("installed", member.GetProperty("status").GetString());
        Assert.Equal("modpack", member.GetProperty("reason").GetString());
        var skipped = Assert.Single(accepted.Json.GetProperty("skipped").EnumerateArray());
        Assert.Equal("save", skipped.GetProperty("section").GetString());
        Assert.Equal("apollo-save", skipped.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("--proceed-with-yanked", "not a valid id")]
    [InlineData("--version", "not-a-version")]
    public async Task PackInstall_InvalidOptionValue_IsAUsageErrorWithoutBuildingServices(string option, string value)
    {
        var run = await _host.RunAsync("pack", "install", "navigation-pack", option, value);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(0, _host.Builds);
    }

    private static FakeInstalledGameVersionProvider Installed(string version) => new()
    {
        Installed = new InstalledGameVersion(GameVersion.Parse(version), version),
    };

    private static ContentIndexPackVersion Usable(ModPackMetadata version) => new(version, null);

    private static ContentIndexPack Pack(params ModPackMetadata[] versions) =>
        Pack(versions.Select(Usable).ToArray());

    private static ContentIndexPack Pack(params ContentIndexPackVersion[] versions) =>
        new(versions[0].Metadata.ModPackId, versions, null);

    private static ContentIndexSnapshot Snapshot(params ContentIndexPack[] packs) =>
        Snapshot(packs, Array.Empty<ContentIndexDiagnostic>());

    private static ContentIndexSnapshot Snapshot(IReadOnlyList<ContentIndexPack> packs, params ContentIndexDiagnostic[] diagnostics) => new(
        1,
        Array.Empty<ContentIndexListing>(),
        packs,
        null,
        diagnostics);

    private static ContentIndexSnapshot WithGameVersions(ContentIndexSnapshot snapshot, params string[] versions) => new(
        snapshot.SnapshotVersion,
        snapshot.Listings,
        snapshot.Packs,
        new ContentIndexGameVersions(1, "https://example.com/version", versions),
        snapshot.Diagnostics);

    public void Dispose() => _host.Dispose();
}
