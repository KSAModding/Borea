using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Borea.App.Formatting;
using Borea.App.Links;
using Borea.App.Tests.Links;
using Borea.App.ViewModels;
using Borea.Core.History;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

public sealed class LinkViewModelTests
{
    private const string MeasureToolsUrl = "https://github.com/Maximilian-Nesslauer/KSA-MeasureTools/releases/download/v1.1.10/MeasureTools.zip";
    private const string MeasureToolsSha256 = "8718558358629EFC3753ACFF9052851EFEB142A9343A1794485C177651265F15";

    [Theory]
    [InlineData("borea://mod/MeasureTools")]
    [InlineData("borea://MOD/measuretools/")]
    public async Task ModLink_OpensTheContentPage(string link)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.SetMainWindowSettings();

        await viewModel.OpenLinkAsync(link);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.False(viewModel.IsSettingsOpen);
        Assert.Equal("MeasureTools", viewModel.SelectedContent!.ModId);
        Assert.Equal("index", viewModel.SelectedContent.Source);
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Theory]
    [InlineData("borea://mod/Measure Tools")]
    [InlineData("borea://launch/MeasureTools")]
    [InlineData("borea://mod/MeasureTools?version=1.1.10")]
    public async Task RefusedLink_ShowsAToast_AndStaysOnThePage(string link)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var requests = harness.Requests.Count;

        await viewModel.OpenLinkAsync(link);

        Assert.True(viewModel.CurrentWindowHome);
        Assert.Equal(harness.Localization.LinkRefused, Assert.Single(viewModel.Toasts.Items).Message);
        Assert.Equal(requests, harness.Requests.Count);
    }

    [Fact]
    public async Task StartWithMoreThanOneArgument_IsRefused()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.OpenStartLinkAsync(["borea://mod/MeasureTools", "--help"]);

        Assert.True(viewModel.CurrentWindowHome);
        Assert.Equal(harness.Localization.LinkRefused, Assert.Single(viewModel.Toasts.Items).Message);
    }

    [Fact]
    public async Task UnknownId_RefreshesOnce_ThenSaysItIsNotInTheIndex()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var requests = harness.Requests.Count;

        await viewModel.OpenLinkAsync("borea://mod/OrbitTools");

        Assert.True(viewModel.CurrentWindowHome);
        Assert.Equal(requests + 1, harness.Requests.Count);
        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.True(toast.IsFailed);
        Assert.Equal(harness.Localization.FormatLinkNotInIndex("OrbitTools"), toast.Message);
    }

    [Fact]
    public async Task IdAddedToTheIndexSinceTheStart_IsFoundAfterTheRefresh()
    {
        var published = false;
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot => published ? WithListing(snapshot, "OrbitTools") : snapshot);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        published = true;

        await viewModel.OpenLinkAsync("borea://mod/orbittools");

        Assert.True(viewModel.CurrentWindowContent);
        Assert.Equal("OrbitTools", viewModel.SelectedContent!.ModId);
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Theory]
    [InlineData("borea://pack/tools-pack")]
    [InlineData("borea://mod/Tools-Pack")]
    public async Task PackOrModLinkToAPack_OpensThePackPage(string link)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPack("tools-pack", "1.0.0", "1.1.10"));
        var viewModel = harness.ViewModel;

        await viewModel.OpenLinkAsync(link);

        Assert.True(viewModel.CurrentWindowPack);
        Assert.Equal("tools-pack", viewModel.SelectedPack!.PackId);
    }

    [Fact]
    public async Task PackLinkToAMod_OpensTheContentPage()
    {
        using var harness = await ViewModelHarness.CreateAsync();

        await harness.ViewModel.OpenLinkAsync("borea://pack/MeasureTools");

        Assert.True(harness.ViewModel.CurrentWindowContent);
    }

    [Fact]
    public async Task InstallLink_WithoutActiveInstance_OpensThePageAndShowsTheHint()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.OpenLinkAsync("borea://install/MeasureTools");

        Assert.True(viewModel.CurrentWindowContent);
        Assert.False(viewModel.SelectedContent!.IsConfirmingInstall);
        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.Equal(viewModel.InstanceHintText, toast.Message);
        Assert.Equal(harness.Localization.DiscoverOpenLibrary, toast.ActionText);
    }

    [Fact]
    public async Task InstallLink_HoldsTheNewestReleaseForAConfirmation_NamingTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await ActivateInstanceAsync(harness);
        var viewModel = harness.ViewModel;

        await viewModel.OpenLinkAsync("borea://install/MeasureTools");

        var row = viewModel.SelectedContent!;
        Assert.True(viewModel.CurrentWindowContent);
        Assert.True(row.IsConfirmingInstall);
        Assert.Equal("1.1.10", row.PendingPlan!.Operations.Single().Release.Version.ToString());
        Assert.Equal(harness.Localization.FormatLinkInstall("MeasureTools 1.1.10", "Main"), row.LinkRequestText);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);

        row.CancelInstallCommand.Execute(null);
        Assert.Null(row.LinkRequestText);
    }

    [Fact]
    public async Task InstallLink_ModLoader_OnlyOpensThePage()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await ActivateInstanceAsync(harness);
        var viewModel = harness.ViewModel;

        await viewModel.OpenLinkAsync("borea://install/StarMap");

        var row = viewModel.SelectedContent!;
        Assert.True(viewModel.CurrentWindowContent);
        Assert.Equal("StarMap", row.ModId);
        Assert.False(row.IsConfirmingInstall);
        Assert.Null(row.PendingPlan);
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Fact]
    public async Task InstallLink_WithAVersion_HoldsThatVersion()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await ActivateInstanceAsync(harness);

        await harness.ViewModel.OpenLinkAsync("borea://install/MeasureTools?version=1.1.9");

        var row = harness.ViewModel.SelectedContent!;
        Assert.Equal("1.1.9", row.PendingPlan!.Operations.Single().Release.Version.ToString());
        Assert.Equal(harness.Localization.FormatLinkInstall("MeasureTools 1.1.9", "Main"), row.LinkRequestText);
    }

    [Fact]
    public async Task InstallLink_VersionNotInTheIndex_OffersTheNewestRelease()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await ActivateInstanceAsync(harness);

        await harness.ViewModel.OpenLinkAsync("borea://install/MeasureTools?version=9.9.9");

        var row = harness.ViewModel.SelectedContent!;
        Assert.Equal("1.1.10", row.PendingPlan!.Operations.Single().Release.Version.ToString());
        Assert.StartsWith(harness.Localization.FormatLinkVersionMissing("9.9.9"), row.LinkRequestText, StringComparison.Ordinal);
        Assert.EndsWith(harness.Localization.FormatLinkInstall("MeasureTools 1.1.10", "Main"), row.LinkRequestText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallLink_YankedVersion_OffersTheNewestRelease()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot => Yank(snapshot, "1.1.9"));
        await ActivateInstanceAsync(harness);

        await harness.ViewModel.OpenLinkAsync("borea://install/MeasureTools?version=1.1.9");

        var row = harness.ViewModel.SelectedContent!;
        Assert.Equal("1.1.10", row.PendingPlan!.Operations.Single().Release.Version.ToString());
        Assert.StartsWith(harness.Localization.FormatLinkVersionYanked("1.1.9"), row.LinkRequestText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallLink_Confirmed_Installs_AndASecondLinkSaysItIsInstalled()
    {
        var archive = Archive();
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == MeasureToolsUrl ? ArchiveResponse(archive) : null,
            editSnapshot: snapshot => WithPack("tools-pack", "1.0.0", "1.1.10")(WithArchive(snapshot, archive)));
        var instance = await ActivateInstanceAsync(harness);
        var viewModel = harness.ViewModel;
        await viewModel.OpenLinkAsync("borea://install/MeasureTools");

        await viewModel.SelectedContent!.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Equal("MeasureTools", Assert.Single((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods).ModId);
        Assert.Null(viewModel.SelectedContent.LinkRequestText);

        await viewModel.OpenLinkAsync("borea://install/MeasureTools");

        Assert.False(viewModel.SelectedContent.IsConfirmingInstall);
        var installed = harness.Localization.FormatLinkAlreadyInstalled("MeasureTools", "Main");
        Assert.Equal(installed, viewModel.Toasts.Items[^1].Message);

        await viewModel.OpenLinkAsync("borea://install/MeasureTools?version=9.9.9");
        Assert.Equal($"{harness.Localization.FormatLinkVersionMissing("9.9.9")} {installed}", viewModel.Toasts.Items[^1].Message);

        await viewModel.OpenLinkAsync("borea://install/tools-pack");
        Assert.False(viewModel.SelectedPack!.IsConfirmingInstall);
        Assert.Equal(harness.Localization.FormatLinkAlreadyInstalled("tools-pack", "Main"), viewModel.Toasts.Items[^1].Message);
    }

    [Fact]
    public async Task InstallLink_Pack_WaitsForTheConfirmation()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPack("tools-pack", "1.0.0", "1.1.10"));
        var instance = await ActivateInstanceAsync(harness);
        var viewModel = harness.ViewModel;

        await viewModel.OpenLinkAsync("borea://install/tools-pack?version=2.0.0");

        var pack = viewModel.SelectedPack!;
        Assert.True(viewModel.CurrentWindowPack);
        Assert.True(pack.IsConfirmingInstall);
        Assert.StartsWith(harness.Localization.FormatLinkVersionMissing("2.0.0"), pack.LinkRequestText, StringComparison.Ordinal);
        Assert.EndsWith(harness.Localization.FormatLinkInstall("tools-pack 1.0.0", "Main"), pack.LinkRequestText, StringComparison.Ordinal);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);

        pack.CancelInstallCommand.Execute(null);
        Assert.False(pack.IsConfirmingInstall);
        Assert.Null(pack.LinkRequestText);
    }

    [Fact]
    public async Task InstallLink_PackVersion_HoldsThatVersion_AndTryAgainKeepsIt()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithPack("tools-pack", "1.0.0", "1.1.10", olderVersion: "0.9.0"));
        await ActivateInstanceAsync(harness);
        var viewModel = harness.ViewModel;

        await viewModel.OpenLinkAsync("borea://install/tools-pack?version=0.9.0");

        var pack = viewModel.SelectedPack!;
        Assert.Equal(harness.Localization.FormatLinkInstall("tools-pack 0.9.0", "Main"), pack.LinkRequestText);
        await pack.ConfirmInstallCommand.ExecuteAsync(null);
        var failed = viewModel.Tasks.History[0];
        Assert.Equal(TaskState.Failed, failed.State);
        Assert.Equal("0.9.0", failed.Version);

        await failed.RetryCommand.ExecuteAsync(null);
        Assert.Equal("0.9.0", pack.PendingInstall!.Pack.Metadata!.Version.ToString());
        await pack.ConfirmInstallCommand.ExecuteAsync(null);

        var again = viewModel.Tasks.History[0];
        Assert.NotSame(failed, again);
        Assert.Equal("0.9.0", again.Version);
    }

    [Fact]
    public async Task InstallLink_PlanWithoutWarnings_StillWaitsForTheConfirmation()
    {
        using var harness = await CreateWithGameAsync(snapshot => Compatible(WithPack("tools-pack", "1.0.0", "1.1.10", gameMin: "2026.8.3.5117")(snapshot)));
        var instance = await ActivateInstanceAsync(harness);
        var viewModel = harness.ViewModel;

        await viewModel.OpenLinkAsync("borea://install/MeasureTools");

        var row = viewModel.SelectedContent!;
        Assert.Null(row.InstallWarning);
        Assert.True(row.IsConfirmingInstall);
        Assert.NotNull(row.LinkRequestText);

        await viewModel.OpenLinkAsync("borea://install/tools-pack");

        var pack = viewModel.SelectedPack!;
        Assert.Null(pack.InstallWarning);
        Assert.True(pack.IsConfirmingInstall);
        Assert.Equal(harness.Localization.FormatLinkInstall("tools-pack 1.0.0", "Main"), pack.LinkRequestText);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task Registration_RunsAtStart_AndTheSettingRemovesIt()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var registrar = new FakeRegistrar();
        var viewModel = NewViewModel(harness, AppPreferences.Empty, registrar);

        await viewModel.LoadAsync();
        await viewModel.LoadAsync();
        await viewModel.WhenLinkRegistrationDoneAsync();
        Assert.True(viewModel.CanRegisterLinks);
        Assert.Equal(["register /opt/Borea/borea"], registrar.Calls);

        viewModel.OpenBoreaLinks = false;
        await viewModel.WhenPreferencesSavedAsync();
        await viewModel.WhenLinkRegistrationDoneAsync();

        Assert.Equal(["register /opt/Borea/borea", "unregister /opt/Borea/borea"], registrar.Calls);
        Assert.False((await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames)).Preferences.OpenBoreaLinks);
    }

    [Fact]
    public async Task Registration_SettingOff_RegistersNothingAtStart()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var registrar = new FakeRegistrar();
        var viewModel = NewViewModel(harness, AppPreferences.Empty.WithOpenBoreaLinks(false), registrar);

        await viewModel.LoadAsync();
        await viewModel.WhenLinkRegistrationDoneAsync();
        Assert.False(viewModel.OpenBoreaLinks);
        Assert.Empty(registrar.Calls);

        viewModel.OpenBoreaLinks = true;
        await viewModel.WhenPreferencesSavedAsync();
        await viewModel.WhenLinkRegistrationDoneAsync();

        Assert.Equal(["register /opt/Borea/borea"], registrar.Calls);
    }

    private static MainViewModel NewViewModel(ViewModelHarness harness, AppPreferences preferences, FakeRegistrar registrar)
        => new(harness.Localization, new RegionalFormatService(harness.Localization), harness.Services.AppPreferences, preferences)
        {
            LinkHandler = new LinkHandler(registrar, "/opt/Borea/borea"),
        };

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

    /// <summary>Makes MeasureTools 1.1.10 support the game of <see cref="CreateWithGameAsync"/>.</summary>
    private static string Compatible(string snapshot)
    {
        var root = JsonNode.Parse(snapshot)!;
        var release = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == "MeasureTools")!["releases"]!.AsArray()
            .Single(node => (string?)node!["version"] == "1.1.10")!;
        release["game_min"] = "2026.8.3.5117";
        release["game_min_revision"] = 5117;
        return root.ToJsonString();
    }

    private static async Task<Instance> ActivateInstanceAsync(ViewModelHarness harness)
    {
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();
        return instance;
    }

    private static string WithListing(string snapshot, string id)
    {
        var root = JsonNode.Parse(snapshot)!;
        var listings = root["listings"]!.AsArray();
        var copy = listings.Single(node => (string?)node!["id"] == "MeasureTools")!.DeepClone();
        copy["id"] = id;
        copy["authored"]!["id"] = id;
        foreach (var release in copy["releases"]!.AsArray())
        {
            release!["id"] = id;
            if (release["listing"] is JsonObject listing)
                listing["id"] = id;
        }
        listings.Add(copy);
        return root.ToJsonString();
    }

    /// <param name="olderVersion">A second pack version that pins MeasureTools 1.1.9.</param>
    private static Func<string, string> WithPack(string id, string version, string measureToolsVersion, string gameMin = "2026.8.19.5261", string? olderVersion = null) => snapshot =>
    {
        const string empty = "\"packs\": []";
        if (!snapshot.Contains(empty, StringComparison.Ordinal))
            throw new InvalidOperationException("The snapshot fixture no longer has an empty packs array.");

        string Version(string packVersion, string pinned) => $$"""{ "authored": { "spec_version": 1, "id": "{{id}}", "type": "modpack", "name": "{{id}}", "authors": ["Maxi"], "abstract": "A pack.", "license": "MIT", "tags": ["starter"], "links": { "forums": "https://forums.example.com/{{id}}" }, "version": "{{packVersion}}", "released_at": "2026-09-01T12:00:00Z", "compatibility": { "game_min": "{{gameMin}}" }, "mods": [{ "id": "MeasureTools", "version": "{{pinned}}" }] } }""";
        var versions = olderVersion is null ? Version(version, measureToolsVersion) : $"{Version(version, measureToolsVersion)}, {Version(olderVersion, "1.1.9")}";
        var pack = $$"""{ "id": "{{id}}", "versions": [{{versions}}] }""";
        return snapshot.Replace(empty, $"\"packs\": [{pack}]", StringComparison.Ordinal);
    };

    private static string Yank(string snapshot, string version)
    {
        var field = $"\"version\": \"{version}\",";
        if (snapshot.Split(field).Length != 2)
            throw new InvalidOperationException($"The snapshot fixture does not name version {version} exactly once.");
        return snapshot.Replace(field, $"{field} \"yanked\": true, \"yanked_reason\": \"Broken.\",", StringComparison.Ordinal);
    }

    private static string WithArchive(string snapshot, byte[] archive)
        => snapshot.Replace(MeasureToolsSha256, Convert.ToHexString(SHA256.HashData(archive)), StringComparison.Ordinal)
            .Replace("\"size\": 41782", $"\"size\": {archive.Length}", StringComparison.Ordinal);

    private static byte[] Archive()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("MeasureTools/mod.toml").Open());
            writer.Write("name = \"MeasureTools\"");
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
