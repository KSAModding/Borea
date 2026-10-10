using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

/// <summary>The install and uninstall steps of RFC 0035 on the mod page, in the confirmations and after an install.</summary>
public sealed class InstallStepsTests
{
    private const string ArchiveHost = "archives.test";
    internal const string QemuStep = "Install QEMU from the package manager of your system.";
    private const string RestartStep = "Start the game once.\nThen close it again.";
    internal const string LibraryStep = "Allow the flight computer through your firewall.";
    internal const string UninstallStep = "Delete the flight plans in your saves folder.";

    private static readonly ConcurrentDictionary<string, byte[]> Archives = new();

    [Fact]
    public async Task ModPage_ShowsTheStepsOfAListing_AndAListingWithoutOrWithAnEmptyListShowsNone()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => WithInstall(WithInstall(json, "MeasureTools", [QemuStep, RestartStep]), "KSArmory", [], []));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        await OpenAsync(viewModel, "MeasureTools");
        var steps = viewModel.SelectedContent!.InstallSteps!;

        Assert.Equal(harness.Localization.ContentInstallSteps, steps.Heading);
        Assert.Equal(["1.", "2."], steps.Steps.Select(step => step.Number));
        Assert.Equal([QemuStep, RestartStep], steps.Steps.Select(step => step.Text));
        Assert.Null(viewModel.SelectedContent.UninstallSteps);

        await OpenAsync(viewModel, "KSArmory");
        Assert.Null(viewModel.SelectedContent!.InstallSteps);
        Assert.Null(viewModel.SelectedContent.UninstallSteps);

        await OpenAsync(viewModel, "AdvancedFlightComputer");
        Assert.Null(viewModel.SelectedContent!.InstallSteps);
        Assert.Null(viewModel.SelectedContent.UninstallSteps);
    }

    [Fact]
    public async Task ModPage_OfALoader_ShowsItsUninstallSteps()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        await OpenAsync(viewModel, "StarMap", ContentType.ModLoader);
        var steps = viewModel.SelectedContent!.UninstallSteps!;

        Assert.Equal(harness.Localization.ContentUninstallSteps, steps.Heading);
        Assert.Equal("Delete the StarMap directory. The game runs unmodded again with no further cleanup.", Assert.Single(steps.Steps).Text);
        Assert.Null(viewModel.SelectedContent.InstallSteps);
    }

    [Fact]
    public async Task ModPage_LanguageChange_TranslatesTheHeading()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => WithInstall(json, "MeasureTools", [QemuStep]));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await OpenAsync(viewModel, "MeasureTools");
        var english = viewModel.SelectedContent!.InstallSteps!.Heading;
        // the install reports its progress through Progress<T>, which the test context runs on pool threads, so the events can come from two threads at once
        var changed = new ConcurrentQueue<string?>();
        viewModel.SelectedContent.PropertyChanged += (_, e) => changed.Enqueue(e.PropertyName);

        harness.Localization.TrySetCulture("de");

        Assert.Contains(nameof(DiscoverItem.InstallSteps), changed);
        Assert.NotEqual(english, viewModel.SelectedContent.InstallSteps!.Heading);
        Assert.Equal(harness.Localization.ContentInstallSteps, viewModel.SelectedContent.InstallSteps.Heading);
    }

    [Fact]
    public async Task Install_WithSteps_WaitsForAConfirmationThatShowsTheStepsOfEveryListingOfThePlan()
    {
        using var harness = await CreateWithGameAsync(json => WithInstall(WithInstall(WithRequired(json, "MeasureTools", "AdvancedFlightComputer"), "MeasureTools", [QemuStep]), "AdvancedFlightComputer", [LibraryStep]));
        var instance = await ActivateInstanceAsync(harness);
        var item = harness.ViewModel.DiscoverItems.Single(row => row.ModId == "MeasureTools");

        await item.InstallCommand.ExecuteAsync(null);

        Assert.True(item.IsConfirmingInstall);
        Assert.Null(item.InstallWarning);
        // in the order the plan installs them, the dependency first
        Assert.Equal(
            [harness.Localization.FormatInstallStepsFor("Advanced Flight Computer"), harness.Localization.FormatInstallStepsFor("MeasureTools")],
            item.PlanSteps.Select(list => list.Heading));
        Assert.Equal([LibraryStep], item.PlanSteps[0].Steps.Select(step => step.Text));
        Assert.Equal([QemuStep], item.PlanSteps[1].Steps.Select(step => step.Text));
        Assert.Empty(await ModIdsAsync(harness, instance));
    }

    [Fact]
    public async Task Install_WithoutSteps_RunsAtOnceAndShowsNoSteps()
    {
        using var harness = await CreateWithGameAsync(json => WithInstall(json, "MeasureTools", [QemuStep]));
        var instance = await ActivateInstanceAsync(harness);
        var item = harness.ViewModel.DiscoverItems.Single(row => row.ModId == "KSArmory");

        await item.InstallCommand.ExecuteAsync(null);

        Assert.False(item.IsConfirmingInstall);
        Assert.Empty(item.PlanSteps);
        Assert.Null(item.InstallError);
        Assert.Equal(["KSArmory"], await ModIdsAsync(harness, instance));
        Assert.False(item.HasInstallStepsNotice);
    }

    [Fact]
    public async Task Install_FromTheVersionsTable_ShowsTheStepsInItsConfirmation()
    {
        using var harness = await CreateWithGameAsync(json => WithInstall(json, "MeasureTools", [QemuStep]));
        await ActivateInstanceAsync(harness);
        var viewModel = harness.ViewModel;
        await OpenAsync(viewModel, "MeasureTools");
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);
        var newest = viewModel.ContentVersions.First();

        await newest.InstallCommand.ExecuteAsync(null);

        Assert.True(newest.IsConfirmingInstall);
        Assert.Equal([QemuStep], Assert.Single(newest.PlanSteps).Steps.Select(step => step.Text));
    }

    [Fact]
    public async Task Install_Finished_LeavesTheStepsOnTheRowUntilTheyAreDismissed()
    {
        using var harness = await CreateWithGameAsync(json => WithInstall(json, "MeasureTools", [QemuStep]));
        var instance = await ActivateInstanceAsync(harness);
        var viewModel = harness.ViewModel;
        var item = viewModel.DiscoverItems.Single(row => row.ModId == "MeasureTools");
        await item.InstallCommand.ExecuteAsync(null);

        // a plan without warnings, choices or added mods still waits, because it has steps
        Assert.True(item.IsConfirmingInstall);
        Assert.Null(item.InstallWarning);
        Assert.Null(item.AddedModsText);
        Assert.Empty(await ModIdsAsync(harness, instance));
        Assert.False(item.HasInstallStepsNotice);
        // the install reports its progress through Progress<T>, which the test context runs on pool threads, so the events can come from two threads at once
        var changed = new ConcurrentQueue<string?>();
        item.PropertyChanged += (_, e) => changed.Enqueue(e.PropertyName);

        await item.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Contains(nameof(DiscoverItem.HasInstallStepsNotice), changed);
        Assert.Null(item.InstallError);
        Assert.Equal(["MeasureTools"], await ModIdsAsync(harness, instance));
        Assert.Empty(item.PlanSteps);
        Assert.True(item.HasInstallStepsNotice);
        Assert.Equal([QemuStep], Assert.Single(item.InstallStepsNotice).Steps.Select(step => step.Text));

        // moving to another page and back, or a reload of the list, keeps the notice
        await OpenAsync(viewModel, "MeasureTools");
        await OpenAsync(viewModel, "KSArmory");
        item.ClearOutcome();
        Assert.True(item.HasInstallStepsNotice);
        changed.Clear();

        item.DismissInstallStepsNoticeCommand.Execute(null);

        Assert.Contains(nameof(DiscoverItem.HasInstallStepsNotice), changed);
        Assert.False(item.HasInstallStepsNotice);
        Assert.Empty(item.InstallStepsNotice);
    }

    [Fact]
    public async Task Remove_TheConfirmationShowsTheUninstallSteps_AndAListingWithoutThemShowsNone()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => WithInstall(json, "AdvancedFlightComputer", [LibraryStep], [UninstallStep]));
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(row => row.ModId == "AdvancedFlightComputer");
        var armory = viewModel.DiscoverItems.Single(row => row.ModId == "KSArmory");

        var steps = afc.RemoveSteps!;
        Assert.Equal(harness.Localization.ContentRemoveSteps, steps.Heading);
        Assert.Equal([UninstallStep], steps.Steps.Select(step => step.Text));
        Assert.Null(armory.RemoveSteps);

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var rows = viewModel.ContentGroups.SelectMany(group => group.Items).ToList();
        Assert.Equal([UninstallStep], rows.Single(row => row.ModId == "AdvancedFlightComputer").RemoveSteps!.Steps.Select(step => step.Text));
        Assert.Null(rows.Single(row => row.ModId == "KSArmory").RemoveSteps);
    }

    [Fact]
    public async Task PackInstall_WithMemberSteps_WaitsForAConfirmationThatShowsThem_AndAPackWithoutStepsRunsAtOnce()
    {
        using var harness = await CreateWithGameAsync(json => WithInstall(WithPacks(json), "MeasureTools", [QemuStep]));
        var instance = await ActivateInstanceAsync(harness);
        var viewModel = harness.ViewModel;
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var armory = viewModel.DiscoverPacks.Single(pack => pack.PackId == "armory-pack");
        var tools = viewModel.DiscoverPacks.Single(pack => pack.PackId == "tools-pack");

        await armory.InstallCommand.ExecuteAsync(null);

        Assert.False(armory.IsConfirmingInstall);
        Assert.Empty(armory.PlanSteps);
        Assert.Null(armory.InstallError);
        Assert.False(armory.HasInstallStepsNotice);
        Assert.Equal(["KSArmory"], await ModIdsAsync(harness, instance));

        await tools.InstallCommand.ExecuteAsync(null);

        // a pack plan without warnings or choices still waits, because a member has steps
        Assert.True(tools.IsConfirmingInstall);
        Assert.Null(tools.InstallWarning);
        Assert.Null(tools.Choices);
        var steps = Assert.Single(tools.PlanSteps);
        Assert.Equal(harness.Localization.FormatInstallStepsFor("MeasureTools"), steps.Heading);
        Assert.Equal([QemuStep], steps.Steps.Select(step => step.Text));
        Assert.Equal(["KSArmory"], await ModIdsAsync(harness, instance));

        tools.CancelInstallCommand.Execute(null);

        Assert.False(tools.IsConfirmingInstall);
        Assert.Empty(tools.PlanSteps);
    }

    [Fact]
    public async Task PackInstall_Finished_LeavesTheMemberStepsOnThePackRowUntilTheyAreDismissed()
    {
        using var harness = await CreateWithGameAsync(json => WithInstall(WithPacks(json), "MeasureTools", [QemuStep]));
        var instance = await ActivateInstanceAsync(harness);
        var viewModel = harness.ViewModel;
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var tools = viewModel.DiscoverPacks.Single(pack => pack.PackId == "tools-pack");
        await tools.InstallCommand.ExecuteAsync(null);
        Assert.False(tools.HasInstallStepsNotice);
        // the install reports its progress through Progress<T>, which the test context runs on pool threads, so the events can come from two threads at once
        var changed = new ConcurrentQueue<string?>();
        tools.PropertyChanged += (_, e) => changed.Enqueue(e.PropertyName);

        await tools.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Contains(nameof(PackItem.HasInstallStepsNotice), changed);
        Assert.Null(tools.InstallError);
        Assert.Equal(["KSArmory", "MeasureTools"], await ModIdsAsync(harness, instance));
        Assert.False(tools.IsConfirmingInstall);
        Assert.True(tools.HasInstallStepsNotice);
        var notice = Assert.Single(tools.InstallStepsNotice);
        Assert.Equal(harness.Localization.FormatInstallStepsFor("MeasureTools"), notice.Heading);
        Assert.Equal([QemuStep], notice.Steps.Select(step => step.Text));

        tools.ClearOutcome();
        Assert.True(tools.HasInstallStepsNotice);
        changed.Clear();

        tools.DismissInstallStepsNoticeCommand.Execute(null);

        Assert.Contains(nameof(PackItem.HasInstallStepsNotice), changed);
        Assert.False(tools.HasInstallStepsNotice);
        Assert.Empty(tools.InstallStepsNotice);
    }

    /// <summary>A pack that pins MeasureTools and KSArmory and one that pins KSArmory only, both compatible with the test game.</summary>
    internal static string WithPacks(string json)
        => PackViewModelTests.WithPacks(
            PackViewModelTests.Pack("tools-pack", "Tools Pack", PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("MeasureTools", "1.1.10"), PackViewModelTests.Pin("KSArmory", "0.8.44"))),
            PackViewModelTests.Pack("armory-pack", "Armory Pack", PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("KSArmory", "0.8.44"))))(json)
            .Replace("\"game_min\": \"2026.8.19.5261\"", "\"game_min\": \"2026.1.1.1\"", StringComparison.Ordinal);

    internal static Task OpenAsync(MainViewModel viewModel, string modId, ContentType type = ContentType.Mod)
    {
        viewModel.DiscoverType = type;
        return viewModel.DiscoverItems.Single(item => item.ModId == modId).OpenCommand.ExecuteAsync(null);
    }

    /// <summary>Gives the listing of <paramref name="modId"/> an [install] table with these steps.</summary>
    internal static string WithInstall(string json, string modId, string[]? steps, string[]? uninstall = null)
    {
        var root = JsonNode.Parse(json)!;
        var install = new JsonObject();
        if (steps is not null)
            install["steps"] = new JsonArray(steps.Select(step => (JsonNode)JsonValue.Create(step)!).ToArray());
        if (uninstall is not null)
            install["uninstall"] = new JsonArray(uninstall.Select(step => (JsonNode)JsonValue.Create(step)!).ToArray());
        Listing(root, modId)["authored"]!["install"] = install;
        return root.ToJsonString();
    }

    private static string WithRequired(string json, string modId, string dependency)
    {
        var root = JsonNode.Parse(json)!;
        Listing(root, modId)["releases"]![0]!["dependencies"] = JsonNode.Parse($$"""[{ "id": "{{dependency}}", "kind": "required", "source": "authored" }]""");
        return root.ToJsonString();
    }

    private static JsonNode Listing(JsonNode root, string modId)
        => root["listings"]!.AsArray().Single(node => (string?)node!["id"] == modId)!;

    /// <summary>
    /// Makes the newest release of every mod compatible with the test game and downloadable
    /// from <see cref="ArchiveHost"/>, so that an install without a question runs to its end.
    /// </summary>
    private static string Installable(string json)
    {
        var root = JsonNode.Parse(json)!;
        foreach (var listing in root["listings"]!.AsArray())
        {
            var modId = (string)listing!["id"]!;
            var newest = listing["releases"]![0]!;
            newest["game_min"] = "2026.1.1.1";
            newest["game_min_revision"] = 1;
            if ((string?)newest["type"] != "mod")
                continue;

            var archive = Archive(modId);
            newest["download"] = new JsonObject
            {
                ["url"] = $"https://{ArchiveHost}/{modId}/archive.zip",
                ["sha256"] = Convert.ToHexString(SHA256.HashData(archive)),
                ["size"] = archive.Length,
                ["content_type"] = "application/zip",
            };
        }

        return root.ToJsonString();
    }

    /// <summary>A zip with the mod folder at its root, the same bytes for every request of one mod.</summary>
    private static byte[] Archive(string modId) => Archives.GetOrAdd(modId, id =>
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry($"{id}/mod.toml").Open(), Encoding.UTF8);
            writer.Write($"name = \"{id}\"");
        }

        return buffer.ToArray();
    });

    private static HttpResponseMessage? ServeArchive(HttpRequestMessage request)
        => request.RequestUri?.Host == ArchiveHost
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Archive(request.RequestUri.Segments[1].TrimEnd('/'))) }
            : null;

    internal static Task<ViewModelHarness> CreateWithGameAsync(Func<string, string> editSnapshot) =>
        ViewModelHarness.CreateAsync(
            services =>
            {
                var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
                File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
                return services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
            },
            respond: ServeArchive,
            editSnapshot: json => Installable(editSnapshot(json)));

    internal static async Task<Instance> ActivateInstanceAsync(ViewModelHarness harness)
    {
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return instance;
    }

    private static async Task<List<string>> ModIdsAsync(ViewModelHarness harness, Instance instance)
        => (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods.Select(mod => mod.ModId).Order(StringComparer.Ordinal).ToList();
}
