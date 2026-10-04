using System.Collections.Specialized;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Core.Game;
using Borea.Core.History;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

public sealed class PackViewModelTests
{
    private const string MeasureToolsUrl = "https://github.com/Maximilian-Nesslauer/KSA-MeasureTools/releases/download/v1.1.10/MeasureTools.zip";
    private const string MeasureToolsSha256 = "8718558358629EFC3753ACFF9052851EFEB142A9343A1794485C177651265F15";
    private const string MeasureTools119Url = "https://github.com/Maximilian-Nesslauer/KSA-MeasureTools/releases/download/v1.1.9/MeasureTools.zip";
    private const string MeasureTools119Sha256 = "0367A612BD8D0A3A2225A92AB8A9292F7EF4FF47238E159301D8F514BA87E04C";

    [Fact]
    public async Task ModpacksTab_ListsThePacksOfTheSnapshot()
    {
        var packs = WithPacks(
            Pack("starter-pack", "Starter Pack", Version("1.0.0", Pin("MeasureTools", "1.1.9")), Version("1.1.0", Pin("MeasureTools", "1.1.10"), Pin("KSArmory", "0.8.44"))),
            Pack("armory-pack", "Armory Pack", Version("1.0.0", Pin("KSArmory", "0.8.44"))));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot =>
            packs(snapshot).Replace("{ \"id\": \"armory-pack\",", "{ \"id\": \"armory-pack\", \"published_at\": \"2026-09-01T12:00:00Z\",", StringComparison.Ordinal));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.Empty(viewModel.DiscoverPacks);

        viewModel.ShowDiscoverModpacksCommand.Execute(null);

        Assert.True(viewModel.IsModpacksTab);
        Assert.Empty(viewModel.DiscoverItems);
        Assert.True(viewModel.HasDiscoverItems);
        Assert.Equal(["armory-pack", "starter-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));
        var starter = viewModel.DiscoverPacks[1];
        Assert.Equal("Starter Pack", starter.Name);
        Assert.Equal("1.1.0", starter.Version);
        Assert.Equal(2, starter.ModCount);
        Assert.Equal(harness.Localization.FormatPackModCount(2), starter.ModCountText);
        Assert.Equal(["Starter"], starter.AllTags);
        Assert.Equal(GameCompatibility.Unknown, starter.Compatibility);
        Assert.False(string.IsNullOrWhiteSpace(starter.ReleasedText));
        Assert.StartsWith("Released on ", starter.ReleasedDateText);
        Assert.Null(starter.PublishedText);
        Assert.StartsWith("Published ", viewModel.DiscoverPacks[0].PublishedText);

        viewModel.SearchText = "armory";
        Assert.Equal(["armory-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));

        viewModel.ShowDiscoverModsCommand.Execute(null);
        Assert.Empty(viewModel.DiscoverPacks);
    }

    [Fact]
    public async Task ModpacksTab_CountsThePacks()
    {
        var packs = WithPacks(
            Pack("starter-pack", "Starter Pack", Version("1.0.0", Pin("MeasureTools", "1.1.9"))),
            Pack("armory-pack", "Armory Pack", Version("1.0.0", Pin("KSArmory", "0.8.44"))));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: packs);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        Assert.Equal("2 modpacks", viewModel.DiscoverCountText);

        viewModel.SearchText = "armory";
        Assert.Equal("1 of 2 modpacks", viewModel.DiscoverCountText);
    }

    [Fact]
    public async Task ModpacksTab_HidesTheCommonCompatibilityOnItsOwn()
    {
        var packs = Enumerable.Range(1, 9).Select(number => Pack($"pack-{number}", $"Pack {number}", Version("1.0.0", Pin("KSArmory", "0.8.44"))))
            .Append(Pack("new-pack", "New Pack", Version("1.0.0", Pin("KSArmory", "0.8.44"))).Replace("\"game_min\": \"2026.8.19.5261\"", "\"game_min\": \"2026.9.7.5402\"", StringComparison.Ordinal));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks([.. packs]));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.True(GameVersion.TryParse("2026.8.22.5348", out var installed));
        await viewModel.RefreshCompatibilityAsync(installed);

        // three mods, one of them compatible, show every chip
        Assert.All(viewModel.DiscoverItems, item => Assert.True(item.ShowsCompatibility));

        viewModel.ShowDiscoverModpacksCommand.Execute(null);

        Assert.Equal(10, viewModel.DiscoverPacks.Count);
        var shown = Assert.Single(viewModel.DiscoverPacks, pack => pack.ShowsCompatibility);
        Assert.Equal("new-pack", shown.PackId);
        Assert.True(shown.IsIncompatible);
        Assert.All(viewModel.DiscoverPacks.Where(pack => !pack.ShowsCompatibility), pack => Assert.True(pack.IsCompatible));
    }

    [Fact]
    public async Task ModpacksTab_SortsByReleaseDateAndFiltersByGameVersion()
    {
        var armory = Pack("armory-pack", "Armory Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")))
            .Replace("\"game_min\": \"2026.8.19.5261\" }", "\"game_min\": \"2026.8.19.5261\", \"game_max\": \"2026.8.22.5348\" }", StringComparison.Ordinal);
        var starter = Pack("starter-pack", "Starter Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10")))
            .Replace("\"released_at\": \"2026-09-01T12:00:00Z\"", "\"released_at\": \"2026-09-10T12:00:00Z\"", StringComparison.Ordinal)
            .Replace("\"game_min\": \"2026.8.19.5261\" }", "\"game_min\": \"2026.8.19.5261\", \"game_max\": \"2026.9.7.5402\" }", StringComparison.Ordinal);
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(armory, starter));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        Assert.Equal(["armory-pack", "starter-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));

        viewModel.SelectDiscoverSortCommand.Execute(DiscoverSortOrder.RecentlyUpdated);
        Assert.Equal(["starter-pack", "armory-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));

        viewModel.DiscoverGameMin = viewModel.GameVersionOptions.Single(build => build.Revision == 5402);
        Assert.Equal(["starter-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));
    }

    [Fact]
    public async Task ModpacksTab_FiltersThatKeepThePacks_LeaveTheRowsAlone()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(
            Pack("starter-pack", "Starter Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10"))),
            Pack("armory-pack", "Armory Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var rows = viewModel.DiscoverPacks.ToList();
        var changes = new List<NotifyCollectionChangedAction>();
        viewModel.DiscoverPacks.CollectionChanged += (_, e) => changes.Add(e.Action);

        viewModel.HideInstalled = true;
        viewModel.HideIncompatible = true;
        viewModel.SearchText = "armory";
        viewModel.SearchText = string.Empty;

        Assert.Equal(rows, viewModel.DiscoverPacks);
        Assert.Equal([NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add], changes);
    }

    [Fact]
    public async Task ModpacksTab_LicenseFilter_ShowsThePacksWhoseLicenseNamesAChosenOne()
    {
        var shared = Pack("shared-pack", "Shared Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")))
            .Replace("\"license\": \"MIT\"", "\"license\": \"MIT AND CC-BY-SA-4.0\"", StringComparison.Ordinal);
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(
            Pack("starter-pack", "Starter Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10"))),
            shared));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        Assert.Contains("CC-BY-SA-4.0", viewModel.LicenseOptions.Select(license => license.Value));

        viewModel.ToggleLicenseCommand.Execute("CC-BY-SA-4.0");
        Assert.Equal(["shared-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));

        viewModel.ToggleLicenseCommand.Execute("MIT");
        Assert.Equal(["shared-pack", "starter-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));

        viewModel.ClearDiscoverFiltersCommand.Execute(null);
        Assert.Equal(["shared-pack", "starter-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));
    }

    [Fact]
    public async Task OpenPack_ShowsTheMembersWithTheirVersions()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(
            Pack("starter-pack", "Starter Pack", Version("1.0.0", Pin("MeasureTools", "1.1.9")), Version("1.1.0", Pin("AdvancedFlightComputer", "0.7.5"), Pin("OrbitTools", "1.0.0")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);

        await pack.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowPack);
        Assert.True(viewModel.IsDiscoverSection);
        Assert.Same(pack, viewModel.SelectedPack);
        Assert.Equal(["AdvancedFlightComputer", "OrbitTools"], viewModel.PackMembers.Select(member => member.ModId));
        Assert.Equal(["0.7.5", "1.0.0"], viewModel.PackMembers.Select(member => member.Version));
        var afc = viewModel.PackMembers[0];
        Assert.Equal("Advanced Flight Computer", afc.Name);
        Assert.False(afc.IsUnlisted);
        Assert.True(afc.CanOpen);
        var unlisted = viewModel.PackMembers[1];
        Assert.True(unlisted.IsUnlisted);
        Assert.False(unlisted.CanOpen);
        Assert.Equal(["1.1.0", "1.0.0"], viewModel.PackVersions.Select(version => version.Version));
        Assert.Equal([harness.Localization.LinkForum], viewModel.PackLinks.Select(link => link.Label));

        await afc.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.False(viewModel.CurrentWindowPack);
        Assert.Equal("AdvancedFlightComputer", viewModel.SelectedContent?.ModId);
    }

    [Fact]
    public async Task Pack_WhoseMembersAreAtTheirNewestRelease_ShowsNoMark()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(
            Pack("starter-pack", "Starter Pack", Version("1.0.0", Pin("AdvancedFlightComputer", "0.7.5"), Pin("MeasureTools", "1.1.10")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);

        await pack.OpenCommand.ExecuteAsync(null);

        Assert.False(pack.HasNewerReleases);
        Assert.Null(pack.NewerReleasesText);
        Assert.All(viewModel.PackMembers, member => Assert.Null(member.NewerText));
    }

    [Fact]
    public async Task Pack_WithNewerMemberReleases_CountsThemOnTheRow_AndNamesThemOnThePage()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(
            Pack("starter-pack", "Starter Pack", Version("1.0.0", Pin("AdvancedFlightComputer", "0.7.4"), Pin("KSArmory", "0.8.44"), Pin("MeasureTools", "1.1.9")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);

        Assert.Equal(harness.Localization.FormatPackNewerReleases(2, 3), pack.NewerReleasesText);
        Assert.Equal("2 of 3 mods have newer releases", pack.NewerReleasesText);

        await pack.OpenCommand.ExecuteAsync(null);

        Assert.Equal(["0.7.5", null, "1.1.10"], viewModel.PackMembers.Select(member => member.NewerVersion));
        Assert.Equal(harness.Localization.FormatPackMemberNewer("0.7.5"), viewModel.PackMembers[0].NewerText);
        Assert.Equal(["0.7.4", "0.8.44", "1.1.9"], viewModel.PackMembers.Select(member => member.Version));
    }

    [Fact]
    public async Task Pack_NewerReleaseCount_IsTheSameInTheStableAndTheTestingChannel()
    {
        var packs = WithPacks(Pack("starter-pack", "Starter Pack", Version("1.0.0", Pin("AdvancedFlightComputer", "0.7.5"), Pin("KSArmory", "0.8.44"), Pin("MeasureTools", "1.1.9"))));
        var testing = SnapshotRelease.Add(new SnapshotRelease("AdvancedFlightComputer", "0.8.0-beta.1", "testing", "2026-09-10T10:00:00Z"));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => testing(packs(json)));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var stable = Assert.Single(viewModel.DiscoverPacks).NewerReleasesText;

        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Testing);
        await viewModel.WhenReleaseChannelSavedAsync();
        Assert.Equal(ReleaseChannel.Testing, harness.Services.Settings.ReleaseChannel);
        var pack = Assert.Single(viewModel.DiscoverPacks);
        await pack.OpenCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatPackNewerReleases(1, 3), stable);
        Assert.Equal(stable, pack.NewerReleasesText);
        Assert.Equal([null, null, "1.1.10"], viewModel.PackMembers.Select(member => member.NewerVersion));
    }

    [Fact]
    public async Task Versions_InstallRow_PutsThatVersionIntoTheActiveInstance()
    {
        var archive = Archive(("MeasureTools/mod.toml", "name = \"MeasureTools\""));
        using var harness = await CreateWithToolsVersionsAsync(archive);
        var viewModel = harness.ViewModel;
        var instance = await ActivateInstanceAsync(harness);
        var pack = await OpenPackVersionsAsync(harness);
        var older = viewModel.PackVersions.Single(version => version.Version == "1.0.0");
        Assert.False(older.IsInstalled);
        Assert.True(older.CanInstall);

        await older.InstallCommand.ExecuteAsync(null);

        Assert.Contains(harness.Localization.PackCompatibilityUnknown, pack.InstallWarning);
        await pack.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(pack.InstallError);
        var mod = Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Equal("MeasureTools", mod.ModId);
        Assert.Equal(ModVersion.Parse("1.1.9"), mod.Version);
        Assert.Equal(InstallReason.ModPack, mod.Reason);
        Assert.True(older.IsInstalled);
        Assert.False(older.CanInstall);
        Assert.False(viewModel.PackVersions.Single(version => version.Version == "1.1.0").IsInstalled);
        Assert.False(pack.IsInstalled);
    }

    [Fact]
    public async Task Versions_NewInstanceRow_CreatesTheInstanceFromThatVersion()
    {
        var archive = Archive(("MeasureTools/mod.toml", "name = \"MeasureTools\""));
        using var harness = await CreateWithToolsVersionsAsync(archive);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await OpenPackVersionsAsync(harness);
        var older = viewModel.PackVersions.Single(version => version.Version == "1.0.0");

        older.NewInstanceCommand.Execute(null);

        Assert.True(viewModel.IsNameModalOpen);
        Assert.Equal("Tools Pack 1.0.0", viewModel.ModalInstanceName);

        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        await viewModel.SelectedPack!.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SelectedPack.InstallError);
        var instance = Assert.Single(await harness.Services.Instances.GetAllAsync());
        Assert.Equal("Tools Pack 1.0.0", instance.Name);
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")), instance.Source);
        Assert.Equal(ModVersion.Parse("1.1.9"), Assert.Single(instance.Mods).Version);
        Assert.True(older.IsInstalled);
    }

    [Fact]
    public async Task Versions_InstanceThatHoldsAnOlderVersion_MarksThatRow()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(ToolsPackVersions()));
        var viewModel = harness.ViewModel;
        await OpenToolsPackInstanceAsync(harness);

        await OpenPackVersionsAsync(harness);

        Assert.True(viewModel.IsPackVersionsTab);
        Assert.True(viewModel.PackVersions.Single(version => version.Version == "1.0.0").IsInstalled);
        Assert.False(viewModel.PackVersions.Single(version => version.Version == "1.1.0").IsInstalled);
    }

    [Fact]
    public async Task OpenPack_GoesBackToDiscover()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(
            Pack("starter-pack", "Starter Pack", Version("1.0.0", Pin("MeasureTools", "1.1.9")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        await Assert.Single(viewModel.DiscoverPacks).OpenCommand.ExecuteAsync(null);
        Assert.True(viewModel.CurrentWindowPack);

        await viewModel.GoBackCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowDiscover);
        Assert.False(viewModel.CurrentWindowPack);
    }

    [Fact]
    public async Task Install_YankedMember_WaitsForConfirmation()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot =>
            WithPacks(Pack("armory-pack", "Armory Pack", Version("1.0.0", Pin("KSArmory", "0.8.44"))))(Yank(snapshot, "0.8.44", "Broken build.")));
        var viewModel = harness.ViewModel;
        var instance = await ActivateInstanceAsync(harness);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);

        await pack.InstallCommand.ExecuteAsync(null);

        Assert.Contains(harness.Localization.FormatPackMemberYanked("KSArmory", "0.8.44", "Broken build."), pack.InstallWarning);
        Assert.Null(pack.InstallError);
        Assert.False(pack.IsInstalling);
        Assert.Empty(pack.Results);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);

        pack.CancelInstallCommand.Execute(null);
        Assert.Null(pack.InstallWarning);

        await pack.InstallCommand.ExecuteAsync(null);
        await pack.ConfirmInstallCommand.ExecuteAsync(null);

        // tests have no network, so the confirmed member gets as far as its download
        Assert.Null(pack.InstallWarning);
        var result = Assert.Single(pack.Results);
        Assert.Equal(ModPackMemberStatus.Failed, result.Status);
        Assert.DoesNotContain("confirmation", result.Message ?? string.Empty);
        Assert.StartsWith(harness.Localization.FormatPackMemberFailed("KSArmory", "0.8.44", null), pack.InstallError);
        Assert.EndsWith(harness.Localization.PackNothingInstalled, pack.InstallError);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task Install_UnconfirmedYankedMember_NamesThatMember()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot =>
            WithPacks(Pack("armory-pack", "Armory Pack", Version("1.0.0", Pin("KSArmory", "0.8.44"), Pin("MeasureTools", "1.1.10"))))(Yank(snapshot, "0.8.44", "Broken build.")));
        var viewModel = harness.ViewModel;
        var instance = await ActivateInstanceAsync(harness);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);
        await pack.InstallCommand.ExecuteAsync(null);
        pack.PendingInstall = pack.PendingInstall! with { ProceedWithYankedMembers = null };

        await pack.ConfirmInstallCommand.ExecuteAsync(null);

        var expected = $"{harness.Localization.FormatPackMemberNotConfirmed("KSArmory", "0.8.44")} {harness.Localization.PackNothingInstalled}";
        Assert.Equal(expected, pack.InstallError);
        Assert.Equal(expected, viewModel.Tasks.History[0].FailureReason);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task Install_MemberWhoseDownloadIsGone_NamesItWithTheDate_AndInstallsFromTheMirror()
    {
        const string mirror = "https://spacedock.info/mod/4319/MeasureTools/download/1.1.10";
        var archive = Archive(("MeasureTools/mod.toml", "name = \"MeasureTools\""));
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == mirror ? ArchiveResponse(archive) : null,
            editSnapshot: snapshot => ViewModelHarness.MarkGone("MeasureTools", "1.1.10", keepMirrors: true)(
                WithPacks(Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10"))))(
                    snapshot.Replace(MeasureToolsSha256, Convert.ToHexString(SHA256.HashData(archive)), StringComparison.Ordinal)
                        .Replace("\"size\": 41782", $"\"size\": {archive.Length}", StringComparison.Ordinal))));
        var viewModel = harness.ViewModel;
        var instance = await ActivateInstanceAsync(harness);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);
        var date = MainViewModel.DateText(ViewModelHarness.GoneSince);

        await pack.OpenCommand.ExecuteAsync(null);
        Assert.Equal(harness.Localization.FormatContentVersionGone(date), Assert.Single(viewModel.PackMembers).GoneText);

        await pack.InstallCommand.ExecuteAsync(null);

        Assert.Contains($"MeasureTools: {string.Format(CultureInfo.CurrentCulture, Resources.InstallMessageUnavailableFormat, "1.1.10", date)}", pack.InstallWarning);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);

        await pack.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(pack.InstallError);
        var mod = Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Equal("1.1.10", mod.Version.ToString());
        Assert.Contains(harness.Requests, uri => uri.AbsoluteUri == mirror);
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsoluteUri == MeasureToolsUrl);
    }

    [Fact]
    public async Task Install_UnlistedMember_NamesItInTheTaskAndTheLogAndInstallsNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(
            Pack("test-pack", "Test Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10"), Pin("KSArmory", "0.8.44"), Pin("NotListedMod", "1.0.0")))));
        var viewModel = harness.ViewModel;
        var instance = await ActivateInstanceAsync(harness);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);
        await pack.InstallCommand.ExecuteAsync(null);

        await pack.ConfirmInstallCommand.ExecuteAsync(null);

        var expected = $"{harness.Localization.FormatPackMemberUnlisted("NotListedMod", "1.0.0")} {harness.Localization.PackNothingInstalled}";
        Assert.Equal(expected, pack.InstallError);
        var task = viewModel.Tasks.History[0];
        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal(expected, task.FailureReason);
        Assert.Contains(harness.Services.Log.ReadRecentLines(100), line =>
            line.Contains($"Pack test-pack 1.0.0 into instance {instance.InstanceId} did not complete, 0 of 3 members done. Blocked by: NotListedMod 1.0.0: The exact pinned release is not listed.", StringComparison.Ordinal));
        Assert.Equal(2, pack.Results.Count(result => result.Status == ModPackMemberStatus.NotAttempted));
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task IncompleteText_NamesAPackReasonOnceAndCountsOnlyTheMembersThatDidNotInstall()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var localization = harness.Localization;
        var retraction = new PlanningMessage("test-pack", PlanningMessageKind.RetractedPack) { Value = "Broken pack." };
        static ModPackMemberResult Member(string id, ModPackMemberStatus status, string? message = null) => new(id, ModVersion.Parse("1.0.0"), InstallReason.ModPack, status, message);
        static ModPackInstallResult Result(ModPackMemberResult[] members, params PlanningMessage[] warnings) => new(Guid.NewGuid(), null, members, warnings, isComplete: false);

        var changed = harness.ViewModel.IncompleteText(Result(
            [Member("First", ModPackMemberStatus.Installed), Member("Second", ModPackMemberStatus.NotAttempted, "The instance changed during pack installation.")]));
        var failed = harness.ViewModel.IncompleteText(Result(
            [Member("First", ModPackMemberStatus.Installed), Member("Second", ModPackMemberStatus.Failed, "The archive hash did not match."), Member("Third", ModPackMemberStatus.NotAttempted, "An earlier operation failed.")]));
        var retracted = harness.ViewModel.IncompleteText(Result(
            [Member("First", ModPackMemberStatus.Unresolved, "Caller confirmation is required for the retracted pack version."), Member("Second", ModPackMemberStatus.Unresolved, "Caller confirmation is required for the retracted pack version.")],
            retraction));

        Assert.Equal($"{localization.PackInstanceChanged} {localization.FormatPackIncomplete(1, 2)}", changed);
        Assert.Equal($"{localization.FormatPackMemberFailed("Second", "1.0.0", "The archive hash did not match.")} {localization.FormatPackIncomplete(1, 3)}", failed);
        Assert.Equal($"test-pack: {PlanningText.Message(retraction)} {localization.PackNothingInstalled}", retracted);
    }

    [Fact]
    public async Task Install_PutsTheMembersIntoTheActiveInstanceAsPackContent()
    {
        var archive = Archive(("MeasureTools/mod.toml", "name = \"MeasureTools\""));
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == MeasureToolsUrl ? ArchiveResponse(archive) : null,
            editSnapshot: snapshot => WithPacks(
                Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10"))),
                Pack("old-tools-pack", "Old Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.9"))))(
                snapshot.Replace(MeasureToolsSha256, Convert.ToHexString(SHA256.HashData(archive)), StringComparison.Ordinal)
                    .Replace("\"size\": 41782", $"\"size\": {archive.Length}", StringComparison.Ordinal)));
        var viewModel = harness.ViewModel;
        var instance = await ActivateInstanceAsync(harness);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = viewModel.DiscoverPacks.Single(item => item.PackId == "tools-pack");
        var oldPack = viewModel.DiscoverPacks.Single(item => item.PackId == "old-tools-pack");

        await pack.InstallCommand.ExecuteAsync(null);

        // without a game the compatibility is unknown, so the install waits for a confirmation
        Assert.Contains(harness.Localization.PackCompatibilityUnknown, pack.InstallWarning);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        await pack.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(pack.InstallWarning);
        Assert.Null(pack.InstallError);
        Assert.Equal(ModPackMemberStatus.Installed, Assert.Single(pack.Results).Status);
        var mod = Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Equal("MeasureTools", mod.ModId);
        Assert.Equal(InstallReason.ModPack, mod.Reason);
        Assert.True(pack.IsInstalled);
        Assert.False(pack.CanInstall);
        Assert.False(oldPack.IsInstalled);

        viewModel.HideInstalled = true;
        Assert.Equal(["old-tools-pack"], viewModel.DiscoverPacks.Select(item => item.PackId));
    }

    [Fact]
    public async Task InstalledInOtherInstances_ShowsPacksAnotherInstanceHoldsInThePinnedVersions()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(
            Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10"))),
            Pack("old-tools-pack", "Old Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.9")))));
        var viewModel = harness.ViewModel;
        var other = (await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance;
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: false, version: "1.1.10", into: other);
        await ActivateInstanceAsync(harness);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);

        viewModel.InstalledInOtherInstances = true;

        Assert.Equal(["tools-pack"], viewModel.DiscoverPacks.Select(item => item.PackId));
    }

    [Fact]
    public async Task NewInstance_CreatesTheActiveInstanceWithThePackSourceAndThePinnedMods()
    {
        var archive = Archive(("MeasureTools/mod.toml", "name = \"MeasureTools\""));
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == MeasureToolsUrl ? ArchiveResponse(archive) : null,
            editSnapshot: snapshot => WithPacks(Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10"))))(
                snapshot.Replace(MeasureToolsSha256, Convert.ToHexString(SHA256.HashData(archive)), StringComparison.Ordinal)
                    .Replace("\"size\": 41782", $"\"size\": {archive.Length}", StringComparison.Ordinal)));
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);

        pack.NewInstanceCommand.Execute(null);

        Assert.True(viewModel.IsNameModalOpen);
        Assert.Equal("Tools Pack", viewModel.ModalInstanceName);

        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsNameModalOpen);
        Assert.StartsWith(harness.Localization.FormatPackCreatesInstance("Tools Pack"), pack.InstallWarning);
        Assert.Contains(harness.Localization.PackCompatibilityUnknown, pack.InstallWarning);
        Assert.Empty(await harness.Services.Instances.GetAllAsync());

        await pack.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(pack.InstallError);
        Assert.Equal(ModPackMemberStatus.Installed, Assert.Single(pack.Results).Status);
        var instance = Assert.Single(await harness.Services.Instances.GetAllAsync());
        Assert.Equal("Tools Pack", instance.Name);
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")), instance.Source);
        var mod = Assert.Single(instance.Mods);
        Assert.Equal("MeasureTools", mod.ModId);
        Assert.Equal(InstallReason.ModPack, mod.Reason);
        Assert.Equal(instance.InstanceId, viewModel.ActiveInstance?.InstanceId);
        Assert.True(pack.IsInstalled);
        Assert.Equal("Tools Pack", viewModel.Tasks.History[0].InstanceName);
        Assert.Contains(viewModel.Toasts.Items, toast => toast.Message == harness.Localization.FormatLibraryNowActive("Tools Pack"));

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatInstanceGroupModpack("Tools Pack", "1.0.0"), viewModel.ContentGroups[0].Title);
    }

    [Fact]
    public async Task NewInstance_AnotherInstanceIsActive_KeepsItActive()
    {
        var archive = Archive(("MeasureTools/mod.toml", "name = \"MeasureTools\""));
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == MeasureToolsUrl ? ArchiveResponse(archive) : null,
            editSnapshot: snapshot => WithPacks(Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10"))))(
                snapshot.Replace(MeasureToolsSha256, Convert.ToHexString(SHA256.HashData(archive)), StringComparison.Ordinal)
                    .Replace("\"size\": 41782", $"\"size\": {archive.Length}", StringComparison.Ordinal)));
        var viewModel = harness.ViewModel;
        var career = await harness.Services.Instances.CreateAsync("Career", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);

        pack.NewInstanceCommand.Execute(null);
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        await pack.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(pack.InstallError);
        Assert.Contains(await harness.Services.Instances.GetAllAsync(), instance => instance.Name == "Tools Pack" && instance.Mods.Count == 1);
        Assert.Equal(career.Instance.InstanceId, viewModel.ActiveInstance?.InstanceId);
        Assert.DoesNotContain(viewModel.Toasts.Items, toast => toast.Message == harness.Localization.FormatLibraryNowActive("Tools Pack"));
    }

    [Fact]
    public async Task NewInstance_NameTakenWhileTheRowWaits_ShowsTheErrorAndCreatesNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10")))));
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);
        pack.NewInstanceCommand.Execute(null);
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        Assert.True(pack.IsConfirmingInstall);
        await harness.Services.Instances.CreateAsync("Tools Pack", InstanceSource.Custom.Value);

        await pack.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.ModalNameTaken, pack.InstallError);
        var instance = Assert.Single(await harness.Services.Instances.GetAllAsync());
        Assert.IsType<InstanceSource.Custom>(instance.Source);
        Assert.Empty(instance.Mods);
    }

    [Fact]
    public async Task NewInstance_TakenOrEmptyName_ShowsTheErrorInTheModal()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10")))));
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("tools pack", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);
        pack.NewInstanceCommand.Execute(null);

        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsNameModalOpen);
        Assert.Equal(harness.Localization.ModalNameTaken, viewModel.InstanceError);
        Assert.Null(pack.InstallWarning);
        Assert.DoesNotContain(viewModel.Tasks.History, task => task.Kind == TaskKind.PackInstall);

        viewModel.ModalInstanceName = " ";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsNameModalOpen);
        Assert.Equal(harness.Localization.ModalNameRequired, viewModel.InstanceError);
        Assert.Single(await harness.Services.Instances.GetAllAsync());
    }

    [Fact]
    public async Task NewInstance_RecommendationCleared_TheButtonShowsTheSmallerSize()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot =>
            Recommend(WithPacks(Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10"))))(snapshot), "MeasureTools", "1.1.10", "AdvancedFlightComputer"));
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);
        pack.NewInstanceCommand.Execute(null);
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        var measureToolsOnly = $"{harness.Localization.InstallAnyway} ({MainViewModel.SizeText(41_782)})";
        Assert.NotEqual(measureToolsOnly, pack.ConfirmInstallText);

        pack.Choices!.Recommended.Single().IsSelected = false;
        await ViewModelHarness.WaitUntilAsync(() => pack.PendingPlan is not null);

        Assert.Equal(measureToolsOnly, pack.ConfirmInstallText);
        Assert.Empty(await harness.Services.Instances.GetAllAsync());
    }

    [Fact]
    public async Task Install_DeselectedRecommendation_InstallsOnlyThePinnedMod()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot =>
            Recommend(WithPacks(Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10"))))(snapshot), "MeasureTools", "1.1.10", "AdvancedFlightComputer"));
        var viewModel = harness.ViewModel;
        await ActivateInstanceAsync(harness);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);

        await pack.InstallCommand.ExecuteAsync(null);

        var recommendation = Assert.Single(pack.Choices!.Recommended);
        Assert.True(recommendation.IsSelected);
        Assert.True(pack.IsConfirmingInstall);
        Assert.Empty(pack.Results);

        recommendation.IsSelected = false;
        await pack.ConfirmInstallCommand.ExecuteAsync(null);

        // tests have no network, so the pinned mod gets as far as its download
        Assert.Null(pack.Choices);
        Assert.Equal(["MeasureTools"], pack.Results.Select(result => result.ModId));
        Assert.Contains(harness.Requests, uri => uri.AbsoluteUri == MeasureToolsUrl);
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsoluteUri.Contains("KSA-AdvancedFlightComputer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_RecommendationCleared_TheButtonShowsTheSmallerSize()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot =>
            Recommend(WithPacks(Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10"))))(snapshot), "MeasureTools", "1.1.10", "AdvancedFlightComputer"));
        var viewModel = harness.ViewModel;
        await ActivateInstanceAsync(harness);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);
        await pack.InstallCommand.ExecuteAsync(null);
        var measureToolsOnly = $"{harness.Localization.InstallAnyway} ({MainViewModel.SizeText(41_782)})";
        Assert.StartsWith($"{harness.Localization.InstallAnyway} (", pack.ConfirmInstallText, StringComparison.Ordinal);
        Assert.NotEqual(measureToolsOnly, pack.ConfirmInstallText);

        pack.Choices!.Recommended.Single().IsSelected = false;
        await ViewModelHarness.WaitUntilAsync(() => pack.PendingPlan is not null);

        Assert.Equal(measureToolsOnly, pack.ConfirmInstallText);
    }

    [Fact]
    public async Task Install_ChoiceChangedWhileConfirmPlans_RunsNothingAndShowsTheNewSize()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot =>
            Recommend(WithPacks(Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10"))))(snapshot), "MeasureTools", "1.1.10", "AdvancedFlightComputer"));
        var viewModel = harness.ViewModel;
        await ActivateInstanceAsync(harness);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);
        await pack.InstallCommand.ExecuteAsync(null);
        var planning = new TaskCompletionSource();
        var lookups = 0;
        harness.SpaceDock.VersionLookup = _ =>
        {
            Interlocked.Increment(ref lookups);
            return planning.Task;
        };

        var confirm = pack.ConfirmInstallCommand.ExecuteAsync(null);
        await ViewModelHarness.WaitUntilAsync(() => Volatile.Read(ref lookups) > 0);
        pack.Choices!.Recommended.Single().IsSelected = false;
        planning.SetResult();
        await confirm;
        await ViewModelHarness.WaitUntilAsync(() => pack.PendingPlan is not null);

        Assert.NotNull(pack.Choices);
        Assert.Equal($"{harness.Localization.InstallAnyway} ({MainViewModel.SizeText(41_782)})", pack.ConfirmInstallText);
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsoluteUri.EndsWith(".zip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_UntestedDeprecatedPack_WaitsForConfirmation()
    {
        var pack = Pack("armory-pack", "Armory Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")))
            .Replace("\"game_min\": \"2026.8.19.5261\" }", "\"game_min\": \"2026.8.0.1\", \"game_max\": \"2026.8.1.1\" }, \"status\": \"deprecated\", \"superseded_by\": \"new-armory-pack\"", StringComparison.Ordinal);
        using var harness = await CreateWithGameAsync(snapshot => Bounds(WithPacks(pack)(snapshot), "KSArmory", "0.8.44", "2026.8.3.5117"));
        var viewModel = harness.ViewModel;
        var instance = await ActivateInstanceAsync(harness);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var row = Assert.Single(viewModel.DiscoverPacks);
        Assert.Equal(GameCompatibility.Untested, row.Compatibility);

        await row.InstallCommand.ExecuteAsync(null);

        Assert.Contains(harness.Localization.FormatPackUntested("2026.8.1.1"), row.InstallWarning);
        Assert.Contains(harness.Localization.FormatPackSuperseded("new-armory-pack"), row.InstallWarning);
        Assert.Null(row.InstallError);
        Assert.Empty(row.Results);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);

        row.CancelInstallCommand.Execute(null);
        Assert.Null(row.InstallWarning);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task Install_IncompatiblePack_IsBlocked()
    {
        using var harness = await CreateWithGameAsync(WithPacks(Pack("armory-pack", "Armory Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")))));
        var viewModel = harness.ViewModel;
        var instance = await ActivateInstanceAsync(harness);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);
        Assert.Equal(GameCompatibility.Incompatible, pack.Compatibility);

        await pack.InstallCommand.ExecuteAsync(null);

        Assert.Equal($"{harness.Localization.FormatPackIncompatible("2026.8.19.5261")} {harness.Localization.FormatPackMemberIncompatible("KSArmory", "0.8.44", "2026.8.19.5261")}", pack.InstallError);
        Assert.Null(pack.InstallWarning);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);

        viewModel.HideIncompatible = true;
        Assert.Empty(viewModel.DiscoverPacks);
    }

    [Fact]
    public async Task ModpacksTab_MonthBound_ResolvesThroughTheGameReleaseList()
    {
        string MonthPack(string id, string month) => Pack(id, id, Version("1.0.0", Pin("StarMap", "0.4.6")))
            .Replace("\"game_min\": \"2026.8.19.5261\"", $"\"game_min\": \"{month}\"", StringComparison.Ordinal);
        using var harness = await CreateWithGameAsync(WithPacks(MonthPack("august-pack", "2026.8"), MonthPack("september-pack", "2026.9"), MonthPack("future-pack", "2027.1")));
        var viewModel = harness.ViewModel;
        await ActivateInstanceAsync(harness);

        viewModel.ShowDiscoverModpacksCommand.Execute(null);

        Assert.Equal(GameCompatibility.Compatible, viewModel.DiscoverPacks.Single(pack => pack.PackId == "august-pack").Compatibility);
        Assert.Equal(GameCompatibility.Incompatible, viewModel.DiscoverPacks.Single(pack => pack.PackId == "september-pack").Compatibility);
        Assert.Equal(GameCompatibility.Unknown, viewModel.DiscoverPacks.Single(pack => pack.PackId == "future-pack").Compatibility);

        viewModel.DiscoverGameMin = viewModel.GameVersionOptions.Single(build => build.Revision == 5402);
        Assert.Equal(["august-pack", "september-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));

        viewModel.DiscoverGameMin = null;
        viewModel.DiscoverGameMax = viewModel.GameVersionOptions.Single(build => build.Revision == 5348);
        Assert.Equal(["august-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));
    }

    [Fact]
    public async Task Pack_PinWithALowerGameMax_IsUntested_AndThePageNamesThePinWithItsBound()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(
            Pack("flight-pack", "Flight Pack", Version("1.0.0", Pin("AdvancedFlightComputer", "0.7.3"), Pin("KSArmory", "0.8.44")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.True(GameVersion.TryParse("2026.9.7.5402", out var installed));
        await viewModel.RefreshCompatibilityAsync(installed);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);

        Assert.Equal(GameCompatibility.Untested, pack.Compatibility);
        Assert.Equal(["AdvancedFlightComputer 0.7.3 supports KSA up to 2026.8.22.5348."], pack.UnfitPinTexts);

        await pack.OpenCommand.ExecuteAsync(null);

        var afc = viewModel.PackMembers.Single(member => member.ModId == "AdvancedFlightComputer");
        Assert.True(afc.IsUntested);
        Assert.Equal(harness.Localization.CompatibilityUntested, afc.CompatibilityText);
        Assert.Equal(harness.Localization.FormatPackMemberUntested("AdvancedFlightComputer", "0.7.3", "2026.8.22.5348"), afc.FitText);
        Assert.Null(viewModel.PackMembers.Single(member => member.ModId == "KSArmory").FitText);
    }

    [Fact]
    public async Task Pack_PinThatNeedsANewerGame_IsIncompatible_AndHideIncompatibleHidesIt()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(
            Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("KSArmory", "0.8.44"), Pin("MeasureTools", "1.1.10"))),
            Pack("armory-pack", "Armory Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.True(GameVersion.TryParse("2026.8.22.5348", out var installed));
        await viewModel.RefreshCompatibilityAsync(installed);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var tools = viewModel.DiscoverPacks.Single(pack => pack.PackId == "tools-pack");

        Assert.Equal(GameCompatibility.Incompatible, tools.Compatibility);
        Assert.Equal([harness.Localization.FormatPackMemberIncompatible("MeasureTools", "1.1.10", "2026.9.4.5400")], tools.UnfitPinTexts);

        viewModel.HideIncompatible = true;

        Assert.Equal(["armory-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));
    }

    [Fact]
    public async Task Pack_WhosePinsAllFit_StaysCompatible()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(
            Pack("flight-pack", "Flight Pack", Version("1.0.0", Pin("AdvancedFlightComputer", "0.7.5"), Pin("KSArmory", "0.8.44"), Pin("MeasureTools", "1.1.10")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.True(GameVersion.TryParse("2026.9.7.5402", out var installed));
        await viewModel.RefreshCompatibilityAsync(installed);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);

        Assert.Equal(GameCompatibility.Compatible, pack.Compatibility);
        Assert.Empty(pack.UnfitPinTexts);

        await pack.OpenCommand.ExecuteAsync(null);

        Assert.All(viewModel.PackMembers, member => Assert.Null(member.FitText));
    }

    [Fact]
    public async Task ModpacksTab_GameVersionFilter_UsesTheBoundsOfThePinsToo()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(
            Pack("old-pack", "Old Pack", Version("1.0.0", Pin("AdvancedFlightComputer", "0.7.3"))),
            Pack("new-pack", "New Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10"))),
            Pack("armory-pack", "Armory Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        Assert.Equal(3, viewModel.DiscoverPacks.Count);

        viewModel.DiscoverGameMin = viewModel.GameVersionOptions.Single(build => build.Revision == 5402);
        Assert.Equal(["armory-pack", "new-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));

        viewModel.DiscoverGameMin = null;
        viewModel.DiscoverGameMax = viewModel.GameVersionOptions.Single(build => build.Revision == 5348);
        Assert.Equal(["armory-pack", "old-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));
    }

    [Fact]
    public async Task Install_UntestedPin_WaitsForConfirmation_AndNamesThePinWithItsBound()
    {
        var pack = Pack("armory-pack", "Armory Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")))
            .Replace("\"game_min\": \"2026.8.19.5261\"", "\"game_min\": \"2026.8.3.5117\"", StringComparison.Ordinal);
        using var harness = await CreateWithGameAsync(snapshot => Bounds(WithPacks(pack)(snapshot), "KSArmory", "0.8.44", "2026.7.10.5056", "2026.8.3.5116"));
        var viewModel = harness.ViewModel;
        var instance = await ActivateInstanceAsync(harness);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var row = Assert.Single(viewModel.DiscoverPacks);
        Assert.Equal(GameCompatibility.Untested, row.Compatibility);

        await row.InstallCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatPackMemberUntested("KSArmory", "0.8.44", "2026.8.3.5116"), row.InstallWarning);
        Assert.Null(row.InstallError);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task Install_PinThatNeedsANewerGame_IsBlocked_AndNamesThePin()
    {
        var pack = Pack("armory-pack", "Armory Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")))
            .Replace("\"game_min\": \"2026.8.19.5261\"", "\"game_min\": \"2026.8.3.5117\"", StringComparison.Ordinal);
        using var harness = await CreateWithGameAsync(WithPacks(pack));
        var viewModel = harness.ViewModel;
        var instance = await ActivateInstanceAsync(harness);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var row = Assert.Single(viewModel.DiscoverPacks);
        Assert.Equal(GameCompatibility.Incompatible, row.Compatibility);

        await row.InstallCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatPackMemberIncompatible("KSArmory", "0.8.44", "2026.8.19.5261"), row.InstallError);
        Assert.Null(row.InstallWarning);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task PackUpdate_NewerVersion_ListsTheChangesAndUpdatesAfterTheConfirmation()
    {
        var archive = Archive(("MeasureTools/mod.toml", "name = \"MeasureTools\""));
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == MeasureToolsUrl ? ArchiveResponse(archive) : null,
            editSnapshot: snapshot => WithPacks(ToolsPackVersions())(
                snapshot.Replace(MeasureToolsSha256, Convert.ToHexString(SHA256.HashData(archive)), StringComparison.Ordinal)
                    .Replace("\"size\": 41782", $"\"size\": {archive.Length}", StringComparison.Ordinal)));
        var viewModel = harness.ViewModel;
        var instance = await OpenToolsPackInstanceAsync(harness);
        var update = viewModel.PackUpdate!;
        Assert.Equal(harness.Localization.FormatPackUpdateAvailable("Tools Pack", "1.1.0"), update.NoticeText);

        await update.UpdateCommand.ExecuteAsync(null);

        Assert.True(update.IsConfirming);
        Assert.Equal(
            [harness.Localization.FormatPackUpdateAdd(viewModel.ContentName("MeasureTools"), "1.1.10"), harness.Localization.FormatPackUpdateRemove(viewModel.ContentName("KSArmory"), "0.8.44")],
            update.ChangeTexts);
        Assert.Equal("KSArmory", Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods).ModId);

        await update.ConfirmUpdateCommand.ExecuteAsync(null);

        var updated = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.1.0")), updated.Source);
        var mod = Assert.Single(updated.Mods);
        Assert.Equal("MeasureTools", mod.ModId);
        Assert.Equal(InstallReason.ModPack, mod.Reason);
        Assert.Null(viewModel.PackUpdate);
        var task = viewModel.Tasks.History[0];
        Assert.Equal(TaskKind.PackUpdate, task.Kind);
        Assert.Equal(TaskState.Finished, task.State);
    }

    [Fact]
    public async Task PackUpdate_PinnedMod_SaysItStaysAtItsVersion()
    {
        var versions = Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")), Version("1.1.0", Pin("KSArmory", "0.9.0"), Pin("MeasureTools", "1.1.10")));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(versions));
        var viewModel = harness.ViewModel;
        var instance = await OpenToolsPackInstanceAsync(harness);
        await viewModel.ContentGroups.SelectMany(group => group.Items).Single().PinCommand.ExecuteAsync(null);

        await viewModel.PackUpdate!.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(
            [harness.Localization.FormatPackUpdateAdd(viewModel.ContentName("MeasureTools"), "1.1.10"), harness.Localization.FormatPackUpdatePinned(viewModel.ContentName("KSArmory"), "0.8.44")],
            viewModel.PackUpdate.ChangeTexts);
        var mod = Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Equal(("KSArmory", ModVersion.Parse("0.8.44"), true), (mod.ModId, mod.Version, mod.IsPinned));
    }

    [Fact]
    public async Task PackUpdate_DetachedMod_SaysTheUpdateLeavesItAlone()
    {
        var versions = Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")), Version("1.1.0", Pin("KSArmory", "0.9.0"), Pin("MeasureTools", "1.1.10")));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(versions));
        var viewModel = harness.ViewModel;
        var instance = await OpenToolsPackInstanceAsync(harness);
        await viewModel.ContentGroups.SelectMany(group => group.Items).Single().DetachCommand.ExecuteAsync(null);

        await viewModel.PackUpdate!.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(
            [harness.Localization.FormatPackUpdateAdd(viewModel.ContentName("MeasureTools"), "1.1.10"), harness.Localization.FormatPackUpdateDetached(viewModel.ContentName("KSArmory"))],
            viewModel.PackUpdate.ChangeTexts);
        var mod = Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Equal(("KSArmory", ModVersion.Parse("0.8.44"), InstallReason.Manual), (mod.ModId, mod.Version, mod.Reason));
    }

    [Fact]
    public async Task Detach_PackMod_MovesToTheModsGroup_AndTheHeaderCountsTheModsThatDiffer()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("KSArmory", "0.8.44"), Pin("MeasureTools", "1.1.9")))));
        var viewModel = harness.ViewModel;
        var instance = await OpenToolsPackInstanceAsync(harness);
        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.True(row.CanDetach);
        Assert.Equal(harness.Localization.FormatInstanceGroupModpack("Tools Pack", "1.0.0"), viewModel.ContentGroups.Single().Title);
        // the pack pins a mod that the instance does not have, so it differs before the detach already
        Assert.Equal(harness.Localization.FormatInstancePackDifference(1, "Tools Pack", "1.0.0"), viewModel.PackDifferenceText);

        await row.DetachCommand.ExecuteAsync(null);

        var detached = viewModel.ContentGroups.Single();
        Assert.Equal(harness.Localization.InstanceGroupMods, detached.Title);
        Assert.False(detached.Items.Single().CanDetach);
        Assert.Equal(harness.Localization.FormatInstancePackDifference(2, "Tools Pack", "1.0.0"), viewModel.PackDifferenceText);
        var saved = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")).WithDetached(["KSArmory"]), saved.Source);
        Assert.Equal(InstallReason.Manual, Assert.Single(saved.Mods).Reason);
    }

    [Fact]
    public async Task Header_DetachedModThatThePackVersionDoesNotPin_DoesNotCount()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(ToolsPackVersions()));
        var release = (await harness.Services.Mods.GetReleaseAsync("KSArmory", ModVersion.Parse("0.8.44")))!;
        var installed = new InstalledMod("KSArmory", release.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, release, ownershipToken: "token");
        // version 1.0.0 of the pack does not pin MeasureTools, so only KSArmory differs from it
        var source = new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")).WithDetached(["KSArmory", "MeasureTools"]);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Tools", source, DateTimeOffset.UnixEpoch, [installed], false);
        await harness.Services.Instances.CreateAsync(instance);
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();

        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatInstancePackDifference(1, "Tools Pack", "1.0.0"), harness.ViewModel.PackDifferenceText);
    }

    [Fact]
    public async Task Detach_ModThatThePackDidNotInstall_IsNotOffered()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(ToolsPackVersions()));
        var viewModel = harness.ViewModel;
        var instance = await OpenToolsPackInstanceAsync(harness);
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea, into: instance);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        Assert.False(viewModel.ContentGroups.SelectMany(group => group.Items).Single(item => item.ModId == "MeasureTools").CanDetach);
        Assert.Null(viewModel.PackDifferenceText);
    }

    [Fact]
    public async Task Attach_DetachedModAtThePackVersion_FollowsThePackAgainWithoutAQuestion()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(ToolsPackVersions()));
        var viewModel = harness.ViewModel;
        var instance = await OpenToolsPackInstanceAsync(harness);
        await viewModel.ContentGroups.SelectMany(group => group.Items).Single().DetachCommand.ExecuteAsync(null);
        var detached = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.True(detached.CanAttach);

        await detached.AttachCommand.ExecuteAsync(null);

        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.Equal(harness.Localization.FormatInstanceGroupModpack("Tools Pack", "1.0.0"), viewModel.ContentGroups.Single().Title);
        Assert.Equal((true, false, null), (row.CanDetach, row.CanAttach, row.PackVersionText));
        Assert.Null(viewModel.PackDifferenceText);
        var saved = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")), saved.Source);
        Assert.Equal(InstallReason.ModPack, Assert.Single(saved.Mods).Reason);
    }

    [Fact]
    public async Task Attach_DetachedModAtAnotherVersion_AsksBeforeItChangesToThePackVersion()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(MeasureToolsPack()));
        var (instance, other) = await OpenDetachedToolsPackInstanceAsync(harness);
        var row = harness.ViewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.True(row.CanAttach);

        await row.AttachCommand.ExecuteAsync(null);

        var waiting = harness.ViewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.Equal(harness.Localization.FormatContentAttachVersion("1.1.10", "Tools Pack", "1.0.0"), waiting.PackVersionText);
        Assert.True(waiting.IsConfirmingUpdate);
        Assert.Equal(harness.Localization.ContentChangeVersion, waiting.ConfirmUpdateText);
        var attached = Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Equal((other, InstallReason.ModPack), (attached.Version, attached.Reason));
        // the confirmation runs the waiting plan the way an update does
        var change = Assert.Single(waiting.PendingPlan!.Operations);
        Assert.Equal(("MeasureTools", ModVersion.Parse("1.1.10")), (change.Release.ModId, change.Release.Version));
    }

    [Fact]
    public async Task Attach_VersionChangeWithoutAPlannerWarning_StillShowsItsConfirmation()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(ToolsPackVersions()));
        await OpenToolsPackInstanceAsync(harness);
        var row = harness.ViewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.False(row.IsConfirmingUpdate);

        row.PackVersionText = harness.Localization.FormatContentAttachVersion("0.8.44", "Tools Pack", "1.0.0");

        Assert.Null(row.InstallWarning);
        Assert.True(row.IsConfirmingUpdate);
        Assert.Equal(harness.Localization.ContentChangeVersion, row.ConfirmUpdateText);
    }

    [Fact]
    public async Task Attach_CancelledVersionChange_KeepsTheModAttachedAtItsVersion()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(MeasureToolsPack()));
        var (instance, other) = await OpenDetachedToolsPackInstanceAsync(harness);
        await harness.ViewModel.ContentGroups.SelectMany(group => group.Items).Single().AttachCommand.ExecuteAsync(null);
        var waiting = harness.ViewModel.ContentGroups.SelectMany(group => group.Items).Single();

        waiting.CancelUpdateCommand.Execute(null);

        Assert.Equal((null, false), (waiting.PackVersionText, waiting.IsConfirmingUpdate));
        var saved = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")), saved.Source);
        Assert.Equal(other, Assert.Single(saved.Mods).Version);
    }

    [Fact]
    public async Task Attach_PackVersionThatNoLongerPinsTheMod_IsNotOffered()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(ToolsPackVersions()));
        var release = (await harness.Services.Mods.GetReleaseAsync("MeasureTools", ModVersion.Parse("1.1.10")))!;
        var installed = new InstalledMod("MeasureTools", release.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, release, ownershipToken: "token");
        // version 1.0.0 of the pack does not pin MeasureTools, so following the pack would remove it
        var source = new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")).WithDetached(["MeasureTools"]);
        await OpenAsActiveAsync(harness, Instance.FromExisting(Guid.NewGuid(), "Tools", source, DateTimeOffset.UnixEpoch, [installed], false));

        var row = harness.ViewModel.ContentGroups.SelectMany(group => group.Items).Single();

        Assert.Equal((true, false), (row.IsDetached, row.CanAttach));
    }

    [Fact]
    public async Task UseNewer_MemberThatTheActivePackInstanceHoldsAtThePin_OffersTheNewerRelease()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.9"), Pin("KSArmory", "0.8.44")))));
        var instance = await ActivateMeasureToolsPackInstanceAsync(harness);
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true, reason: InstallReason.ModPack, ownership: ModInstallOwnership.Borea, into: instance);
        await harness.ViewModel.LoadAsync();

        var row = await OpenMeasureToolsMemberAsync(harness);

        Assert.Equal((true, true), (row.OffersUseNewer, row.CanUseNewer));
        Assert.Equal(harness.Localization.FormatPackMemberUseNewerChip("1.1.10"), row.UseNewerChipText);
        Assert.Equal(harness.Localization.FormatPackMemberUseNewer("1.1.10", "Tools"), row.UseNewerText);
        Assert.Null(row.PinnedInInstanceText);
        // the instance holds KSArmory at the pin too, but the pin is its newest release
        var armory = harness.ViewModel.PackMembers.Single(member => member.ModId == "KSArmory");
        Assert.Equal((true, false, false, null, null), (armory.IsInstalled, armory.OffersUseNewer, armory.CanUseNewer, armory.UseNewerText, armory.UseNewerChipText));
    }

    [Fact]
    public async Task UseNewer_InstanceThatIsNotOfThePackOrHoldsAnotherVersion_IsNotOffered()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(MeasureToolsPinPack()));

        var row = await OpenMeasureToolsMemberAsync(harness);
        Assert.Equal("1.1.10", row.NewerVersion);
        Assert.False(row.CanUseNewer);

        await ActivateMeasureToolsPackInstanceAsync(harness, "Custom", InstanceSource.Custom.Value);
        Assert.False((await OpenMeasureToolsMemberAsync(harness)).CanUseNewer);

        await ActivateMeasureToolsPackInstanceAsync(harness, "Other pack", new InstanceSource.FromModPack("other-pack", ModVersion.Parse("1.0.0")));
        Assert.False((await OpenMeasureToolsMemberAsync(harness)).CanUseNewer);

        await ActivateMeasureToolsPackInstanceAsync(harness, "Older", version: "1.1.8");
        row = await OpenMeasureToolsMemberAsync(harness);
        Assert.Equal((false, false, null, null, null), (row.OffersUseNewer, row.CanUseNewer, row.UseNewerText, row.UseNewerChipText, row.PinnedInInstanceText));
    }

    [Fact]
    public async Task UseNewer_NewerReleaseThatNeedsANewerGame_IsNotOffered()
    {
        using var harness = await CreateWithGameAsync(WithPacks(MeasureToolsPinPack()));
        await ActivateMeasureToolsPackInstanceAsync(harness);

        var row = await OpenMeasureToolsMemberAsync(harness);

        Assert.Equal("1.1.10", row.NewerVersion);
        Assert.Equal((false, null), (row.CanUseNewer, row.UseNewerText));
    }

    [Fact]
    public async Task UseNewer_Confirmed_DetachesTheModAndInstallsTheNewerRelease()
    {
        var archive = Archive(("MeasureTools/mod.toml", "name = \"MeasureTools\""));
        using var harness = await CreateWithMeasureToolsArchiveAsync(archive, MeasureToolsPinPack());
        var instance = await ActivateMeasureToolsPackInstanceAsync(harness);
        var row = await OpenMeasureToolsMemberAsync(harness);

        await row.UseNewerCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingInstall);
        // the chip stays in the row while the change waits, but it cannot start the change again
        Assert.Equal((true, false), (row.OffersUseNewer, row.CanUseNewer));
        Assert.Equal(harness.Localization.FormatPackMemberUseNewerConfirm(row.Name, "Tools", "1.1.10"), row.UseNewerConfirmText);
        var replace = row.InstallWarning is null ? harness.Localization.FormatContentReplaceVersion("1.1.9") : harness.Localization.FormatContentReplaceVersionAnyway("1.1.9");
        Assert.StartsWith(replace, row.ConfirmInstallText);
        // the files of 1.1.9 go away only after the confirmation
        var waiting = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")), waiting.Source);
        Assert.Equal((ModVersion.Parse("1.1.9"), InstallReason.ModPack), (waiting.Mods.Single().Version, waiting.Mods.Single().Reason));

        await row.ConfirmUseNewerCommand.ExecuteAsync(null);

        Assert.Null(row.InstallError);
        Assert.False(row.IsConfirmingInstall);
        var saved = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")).WithDetached(["MeasureTools"]), saved.Source);
        var mod = Assert.Single(saved.Mods);
        Assert.Equal((ModVersion.Parse("1.1.10"), InstallReason.Manual), (mod.Version, mod.Reason));
        Assert.Equal((false, false), (row.IsInstalled, row.CanUseNewer));
        Assert.Equal(TaskState.Finished, harness.ViewModel.Tasks.History[0].State);

        // the Content tab offers to undo the change
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var content = harness.ViewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.Equal((true, true), (content.IsDetached, content.CanAttach));
    }

    [Fact]
    public async Task UseNewer_Cancelled_ChangesNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(MeasureToolsPinPack()));
        var instance = await ActivateMeasureToolsPackInstanceAsync(harness);
        var row = await OpenMeasureToolsMemberAsync(harness);
        await row.UseNewerCommand.ExecuteAsync(null);
        Assert.True(row.IsConfirmingInstall);

        row.CancelUseNewerCommand.Execute(null);

        Assert.Equal((false, true), (row.IsConfirmingInstall, row.CanUseNewer));
        var saved = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")), saved.Source);
        var mod = Assert.Single(saved.Mods);
        Assert.Equal((ModVersion.Parse("1.1.9"), InstallReason.ModPack), (mod.Version, mod.Reason));
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsoluteUri == MeasureToolsUrl);
    }

    [Fact]
    public async Task UseNewer_InstanceChangedBeforeTheConfirmation_KeepsTheModAttached()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(MeasureToolsPinPack()));
        var instance = await ActivateMeasureToolsPackInstanceAsync(harness);
        var row = await OpenMeasureToolsMemberAsync(harness);
        await row.UseNewerCommand.ExecuteAsync(null);
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.SetPinned("MeasureTools", true));

        await row.ConfirmUseNewerCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.ManualInstallsInstanceChanged, row.InstallError);
        Assert.False(row.IsConfirmingInstall);
        var saved = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")), saved.Source);
        var mod = Assert.Single(saved.Mods);
        Assert.Equal((ModVersion.Parse("1.1.9"), InstallReason.ModPack), (mod.Version, mod.Reason));
    }

    [Fact]
    public async Task UseNewer_LaterPackUpdate_LeavesTheModAtTheNewerRelease()
    {
        var archive = Archive(("MeasureTools/mod.toml", "name = \"MeasureTools\""));
        var versions = Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.9")), Version("1.1.0", Pin("MeasureTools", "1.1.9")));
        using var harness = await CreateWithMeasureToolsArchiveAsync(archive, versions);
        var viewModel = harness.ViewModel;
        var instance = await ActivateMeasureToolsPackInstanceAsync(harness);
        var row = await OpenMeasureToolsMemberAsync(harness);
        await row.UseNewerCommand.ExecuteAsync(null);
        await row.ConfirmUseNewerCommand.ExecuteAsync(null);
        Assert.Null(row.InstallError);

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.PackUpdate!.UpdateCommand.ExecuteAsync(null);
        Assert.Equal([harness.Localization.FormatPackUpdateDetached(viewModel.ContentName("MeasureTools"))], viewModel.PackUpdate.ChangeTexts);
        await viewModel.PackUpdate.ConfirmUpdateCommand.ExecuteAsync(null);

        var saved = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.1.0")).WithDetached(["MeasureTools"]), saved.Source);
        var mod = Assert.Single(saved.Mods);
        Assert.Equal((ModVersion.Parse("1.1.10"), InstallReason.Manual), (mod.Version, mod.Reason));
    }

    [Fact]
    public async Task UseNewer_ConfirmedWhileTheInstanceUpdates_SaysSoAndKeepsThePlan()
    {
        using var download = new ManualResetEventSlim();
        var archive = Archive(("MeasureTools/mod.toml", "name = \"MeasureTools\""));
        using var harness = await CreateWithMeasureToolsArchiveAsync(archive, MeasureToolsPinPack(), request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/AdvancedFlightComputer.zip", StringComparison.Ordinal) == true)
                download.Wait(TimeSpan.FromSeconds(30));
            return null;
        });
        var viewModel = harness.ViewModel;
        var instance = await ActivateMeasureToolsPackInstanceAsync(harness);
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4", into: instance);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();
        var afc = viewModel.ContentGroups.SelectMany(group => group.Items).Single(item => item.ModId == "AdvancedFlightComputer");
        await afc.UpdateCommand.ExecuteAsync(null);
        var update = afc.ConfirmUpdateCommand.ExecuteAsync(null);
        for (var wait = 0; wait < 300 && !harness.Requests.Any(uri => uri.AbsolutePath.EndsWith("/AdvancedFlightComputer.zip", StringComparison.Ordinal)); wait++)
            await Task.Delay(100);
        var row = await OpenMeasureToolsMemberAsync(harness);
        await row.UseNewerCommand.ExecuteAsync(null);

        await row.ConfirmUseNewerCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.LibraryFolderInstanceBusy, row.InstallError);
        Assert.True(row.IsConfirmingInstall);
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")), (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Source);

        // the update fails without its archive and leaves the instance as it was, so the waiting plan still fits
        download.Set();
        await update;
        await row.ConfirmUseNewerCommand.ExecuteAsync(null);

        Assert.Null(row.InstallError);
        var saved = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")).WithDetached(["MeasureTools"]), saved.Source);
        Assert.Equal(ModVersion.Parse("1.1.10"), saved.Mods.Single(mod => mod.ModId == "MeasureTools").Version);
    }

    [Fact]
    public async Task UseNewer_MemberThatIsDetachedAtThePin_ChangesOnlyItsVersion()
    {
        var archive = Archive(("MeasureTools/mod.toml", "name = \"MeasureTools\""));
        using var harness = await CreateWithMeasureToolsArchiveAsync(archive, MeasureToolsPinPack());
        var instance = await ActivateMeasureToolsPackInstanceAsync(harness);
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.DetachFromModPack("MeasureTools"));
        await harness.ViewModel.LoadAsync();
        var row = await OpenMeasureToolsMemberAsync(harness);
        Assert.True(row.CanUseNewer);

        await row.UseNewerCommand.ExecuteAsync(null);

        // the mod is detached already, so the confirmation only names the version that goes away
        Assert.True(row.IsConfirmingInstall);
        Assert.Null(row.UseNewerConfirmText);
        var replace = row.InstallWarning is null ? harness.Localization.FormatContentReplaceVersion("1.1.9") : harness.Localization.FormatContentReplaceVersionAnyway("1.1.9");
        Assert.StartsWith(replace, row.ConfirmInstallText);

        await row.ConfirmUseNewerCommand.ExecuteAsync(null);

        Assert.Null(row.InstallError);
        var saved = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")).WithDetached(["MeasureTools"]), saved.Source);
        var mod = Assert.Single(saved.Mods);
        Assert.Equal((ModVersion.Parse("1.1.10"), InstallReason.Manual), (mod.Version, mod.Reason));
    }

    [Fact]
    public async Task UseNewer_MemberThatTheInstancePins_ShowsThePinnedChipInsteadOfTheUseChip()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(MeasureToolsPinPack()));
        var instance = await ActivateMeasureToolsPackInstanceAsync(harness, pinned: true);
        var row = await OpenMeasureToolsMemberAsync(harness);

        Assert.Equal((false, false, null, null), (row.OffersUseNewer, row.CanUseNewer, row.UseNewerText, row.UseNewerChipText));
        Assert.Equal(harness.Localization.FormatPackMemberUseNewerPinned("Tools", "1.1.10"), row.PinnedInInstanceText);

        await row.UseNewerCommand.ExecuteAsync(null);

        Assert.False(row.IsConfirmingInstall);
        var mod = Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Equal((ModVersion.Parse("1.1.9"), InstallReason.ModPack, true), (mod.Version, mod.Reason, mod.IsPinned));

        // unpinning on the Content tab brings the chip back
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await harness.ViewModel.ContentGroups.SelectMany(group => group.Items).Single().UnpinCommand.ExecuteAsync(null);

        Assert.True(row.CanUseNewer);
        Assert.Null(row.PinnedInInstanceText);
    }

    [Fact]
    public async Task PackUpdate_FailedDownload_KeepsTheOldSourceTheDroppedModAndTheNotice()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(ToolsPackVersions()));
        var viewModel = harness.ViewModel;
        var instance = await OpenToolsPackInstanceAsync(harness);

        await viewModel.PackUpdate!.UpdateCommand.ExecuteAsync(null);
        await viewModel.PackUpdate.ConfirmUpdateCommand.ExecuteAsync(null);

        // tests have no network, so the added mod gets as far as its download
        var afterFailure = (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!;
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")), afterFailure.Source);
        Assert.Equal("KSArmory", Assert.Single(afterFailure.Mods).ModId);
        Assert.NotNull(viewModel.PackUpdate);
        Assert.Contains(harness.Localization.FormatPackUpdateStillInstalled(viewModel.ContentName("KSArmory")), viewModel.PackUpdate.InstallError);
        Assert.False(viewModel.PackUpdate.IsConfirming);
        Assert.Equal(TaskState.Failed, viewModel.Tasks.History[0].State);
    }

    [Fact]
    public async Task PackUpdate_RetractedNewerVersion_ShowsNoNotice()
    {
        var retracted = Version("1.1.0", Pin("MeasureTools", "1.1.10"));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(Pack("tools-pack", "Tools Pack",
            Version("1.0.0", Pin("KSArmory", "0.8.44")),
            (id, name) => retracted(id, name).Replace("{ \"authored\"", "{ \"index_status\": { \"state\": \"retracted\", \"reason\": \"Broken.\" }, \"authored\"", StringComparison.Ordinal))));

        await OpenToolsPackInstanceAsync(harness);

        Assert.Null(harness.ViewModel.PackUpdate);
    }

    [Fact]
    public async Task PackUpdate_VersionWithAPinThatNeedsANewerGame_IsBlocked_AndNamesThePin()
    {
        using var harness = await CreateWithGameAsync(snapshot => WithPacks(ToolsPackVersions())(snapshot)
            .Replace("\"game_min\": \"2026.8.19.5261\" }, \"mods\"", "\"game_min\": \"2026.8.3.5117\" }, \"mods\"", StringComparison.Ordinal));
        var viewModel = harness.ViewModel;
        var instance = await OpenToolsPackInstanceAsync(harness);
        var update = viewModel.PackUpdate!;

        await update.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatPackMemberIncompatible("MeasureTools", "1.1.10", "2026.9.4.5400"), update.InstallError);
        Assert.False(update.IsConfirming);
        Assert.Equal(new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")), (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Source);
    }

    [Fact]
    public async Task PackUpdate_VersionThatAnIndexRefreshPublishes_ShowsTheNoticeOnTheOpenPage()
    {
        var packs = Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot => WithPacks(packs)(snapshot));
        var viewModel = harness.ViewModel;
        await OpenToolsPackInstanceAsync(harness);
        await viewModel.WhenContentUpdatesCheckedAsync();
        Assert.Null(viewModel.PackUpdate);

        packs = ToolsPackVersions();
        await viewModel.RetryContentIndexCommand.ExecuteAsync(null);
        await viewModel.WhenContentUpdatesCheckedAsync();

        Assert.Equal("1.1.0", viewModel.PackUpdate?.Version);
    }

    [Fact]
    public async Task PackUpdate_OpeningAnotherInstance_HidesTheNoticeAtOnce()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPacks(ToolsPackVersions()));
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value);
        await OpenToolsPackInstanceAsync(harness);
        Assert.NotNull(viewModel.PackUpdate);

        var opening = viewModel.Instances.Single(instance => instance.Name == "Other").OpenCommand.ExecuteAsync(null);

        Assert.Null(viewModel.PackUpdate);
        await opening;
        Assert.Null(viewModel.PackUpdate);
    }

    /// <summary>A pack whose 1.0.0 pins MeasureTools 1.1.9 and whose 1.1.0 pins 1.1.10, with the 1.1.9 archive served.</summary>
    private static Task<ViewModelHarness> CreateWithToolsVersionsAsync(byte[] archive) =>
        ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == MeasureTools119Url ? ArchiveResponse(archive) : null,
            editSnapshot: snapshot => WithPacks(Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.9")), Version("1.1.0", Pin("MeasureTools", "1.1.10"))))(
                snapshot.Replace(MeasureTools119Sha256, Convert.ToHexString(SHA256.HashData(archive)), StringComparison.Ordinal)
                    .Replace("\"size\": 41783", $"\"size\": {archive.Length}", StringComparison.Ordinal)));

    /// <summary>Tools Pack 1.0.0 pinning MeasureTools 1.1.9, whose newer release 1.1.10 the snapshot has.</summary>
    private static string MeasureToolsPinPack()
        => Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.9")));

    /// <summary>A harness that serves the archive of MeasureTools 1.1.10, answers other requests with <paramref name="respond"/> and has the pack.</summary>
    private static Task<ViewModelHarness> CreateWithMeasureToolsArchiveAsync(byte[] archive, string pack, Func<HttpRequestMessage, HttpResponseMessage?>? respond = null) =>
        ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == MeasureToolsUrl ? ArchiveResponse(archive) : respond?.Invoke(request),
            editSnapshot: snapshot => WithPacks(pack)(
                snapshot.Replace(MeasureToolsSha256, Convert.ToHexString(SHA256.HashData(archive)), StringComparison.Ordinal)
                    .Replace("\"size\": 41782", $"\"size\": {archive.Length}", StringComparison.Ordinal)));

    /// <summary>
    /// Makes an instance that holds MeasureTools as a pack member in files Borea owns the active one.
    /// Without a source the instance was made from Tools Pack 1.0.0.
    /// </summary>
    private static async Task<Instance> ActivateMeasureToolsPackInstanceAsync(ViewModelHarness harness, string name = "Tools", InstanceSource? source = null, string version = "1.1.9", bool pinned = false)
    {
        var instance = (await harness.Services.Instances.CreateAsync(name, source ?? new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")))).Instance;
        var reason = instance.Source is InstanceSource.FromModPack ? InstallReason.ModPack : InstallReason.Manual;
        instance = await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, reason: reason, ownership: ModInstallOwnership.Borea, version: version, into: instance);
        if (pinned)
            await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved => saved.SetPinned("MeasureTools", true));
        await harness.ViewModel.LoadAsync();
        return instance;
    }

    /// <summary>Opens the only pack of the snapshot on its Mods tab and returns the row of MeasureTools.</summary>
    private static async Task<PackMemberItem> OpenMeasureToolsMemberAsync(ViewModelHarness harness)
    {
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        await Assert.Single(viewModel.DiscoverPacks).OpenCommand.ExecuteAsync(null);
        viewModel.ShowPackModsCommand.Execute(null);
        return viewModel.PackMembers.Single(member => member.ModId == "MeasureTools");
    }

    /// <summary>Opens the only pack of the snapshot on its Versions tab.</summary>
    private static async Task<PackItem> OpenPackVersionsAsync(ViewModelHarness harness)
    {
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);
        await pack.OpenCommand.ExecuteAsync(null);
        viewModel.ShowPackVersionsCommand.Execute(null);
        return pack;
    }

    private static string ToolsPackVersions()
        => Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")), Version("1.1.0", Pin("MeasureTools", "1.1.10")));

    /// <summary>Tools Pack 1.0.0 pinning MeasureTools 1.1.10, which the snapshot has next to older releases.</summary>
    private static string MeasureToolsPack()
        => Pack("tools-pack", "Tools Pack", Version("1.0.0", Pin("MeasureTools", "1.1.10")));

    /// <summary>An active instance of <see cref="MeasureToolsPack"/> whose MeasureTools is detached at 1.1.9, opened on its page.</summary>
    private static async Task<(Instance Instance, ModVersion Version)> OpenDetachedToolsPackInstanceAsync(ViewModelHarness harness)
    {
        var other = (await harness.Services.Mods.GetReleaseAsync("MeasureTools", ModVersion.Parse("1.1.9")))!;
        var installed = new InstalledMod("MeasureTools", other.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, other, ownershipToken: "token");
        var source = new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")).WithDetached(["MeasureTools"]);
        var instance = Instance.FromExisting(Guid.NewGuid(), "Tools", source, DateTimeOffset.UnixEpoch, [installed], false);
        await OpenAsActiveAsync(harness, instance);
        return (instance, other.Version);
    }

    private static async Task OpenAsActiveAsync(ViewModelHarness harness, Instance instance)
    {
        await harness.Services.Instances.CreateAsync(instance);
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
    }

    /// <summary>An active instance created from Tools Pack 1.0.0 with its pinned mod, opened on its page.</summary>
    private static async Task<Instance> OpenToolsPackInstanceAsync(ViewModelHarness harness)
    {
        var release = (await harness.Services.Mods.GetReleaseAsync("KSArmory", ModVersion.Parse("0.8.44")))!;
        var installed = new InstalledMod("KSArmory", release.Version, InstallReason.ModPack, DateTimeOffset.UnixEpoch, release, ownershipToken: "token");
        var instance = Instance.FromExisting(Guid.NewGuid(), "Tools", new InstanceSource.FromModPack("tools-pack", ModVersion.Parse("1.0.0")), DateTimeOffset.UnixEpoch, [installed], false);
        await harness.Services.Instances.CreateAsync(instance);
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        return instance;
    }

    /// <summary>A harness whose game folder holds a game of version 2026.8.3.5117.</summary>
    private static Task<ViewModelHarness> CreateWithGameAsync(Func<string, string> editSnapshot) =>
        ViewModelHarness.CreateAsync(
            services =>
            {
                var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
                File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
                return services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
            },
            editSnapshot: editSnapshot);

    private static async Task<Instance> ActivateInstanceAsync(ViewModelHarness harness)
    {
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return instance;
    }

    internal static Func<string, string> WithPacks(params string[] packs) => snapshot =>
    {
        const string empty = "\"packs\": []";
        if (!snapshot.Contains(empty, StringComparison.Ordinal))
            throw new InvalidOperationException("The snapshot fixture no longer has an empty packs array.");
        return snapshot.Replace(empty, $"\"packs\": [{string.Join(", ", packs)}]", StringComparison.Ordinal);
    };

    private static string Yank(string snapshot, string version, string reason)
    {
        var field = $"\"version\": \"{version}\",";
        if (snapshot.Split(field).Length != 2)
            throw new InvalidOperationException($"The snapshot fixture does not name version {version} exactly once.");
        return snapshot.Replace(field, $"{field} \"yanked\": true, \"yanked_reason\": \"{reason}\",", StringComparison.Ordinal);
    }

    /// <summary>Gives one release of the snapshot other game bounds, with the revisions the stamp derives from them.</summary>
    private static string Bounds(string snapshot, string modId, string version, string gameMin, string? gameMax = null)
    {
        var root = JsonNode.Parse(snapshot)!;
        var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == modId)!;
        var release = listing["releases"]!.AsArray().Single(node => (string?)node!["version"] == version)!;
        release["game_min"] = gameMin;
        release["game_min_revision"] = Revision(gameMin);
        release["game_max"] = gameMax;
        release["game_max_revision"] = gameMax is null ? null : Revision(gameMax);
        return root.ToJsonString();

        static int Revision(string bound) => GameVersion.TryParse(bound, out var parsed) ? parsed.Revision : throw new ArgumentException($"{bound} is not a full game version.", nameof(bound));
    }

    private static string Recommend(string snapshot, string modId, string version, string recommended)
    {
        var root = JsonNode.Parse(snapshot)!;
        var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == modId)!;
        var release = listing["releases"]!.AsArray().Single(node => (string?)node!["version"] == version)!;
        release["dependencies"] = new JsonArray(new JsonObject { ["id"] = recommended, ["kind"] = "recommends", ["source"] = "authored" });
        return root.ToJsonString();
    }

    internal static string Pack(string id, string name, params Func<string, string, string>[] versions) =>
        $$"""{ "id": "{{id}}", "versions": [{{string.Join(", ", versions.Select(version => version(id, name)))}}] }""";

    internal static Func<string, string, string> Version(string version, params string[] pins) => (id, name) =>
        $$"""{ "authored": { "spec_version": 1, "id": "{{id}}", "type": "modpack", "name": "{{name}}", "authors": ["Maxi"], "abstract": "{{name}} abstract.", "description": "## {{name}}", "license": "MIT", "tags": ["starter"], "version": "{{version}}", "released_at": "2026-09-01T12:00:00Z", "links": { "forums": "https://forums.example.com/{{id}}" }, "compatibility": { "game_min": "2026.8.19.5261" }, "mods": [{{string.Join(", ", pins)}}] } }""";

    internal static string Pin(string id, string version) => $$"""{ "id": "{{id}}", "version": "{{version}}" }""";

    private static byte[] Archive(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static HttpResponseMessage ArchiveResponse(byte[] archive)
    {
        var content = new ByteArrayContent(archive);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
