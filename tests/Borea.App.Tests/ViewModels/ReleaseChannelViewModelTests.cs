using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Settings;

namespace Borea.App.Tests.ViewModels;

public sealed class ReleaseChannelViewModelTests
{
    [Fact]
    public async Task ChannelChange_IsSavedAndTheNextInstallUsesIt()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: SnapshotRelease.Add(new SnapshotRelease("AdvancedFlightComputer", "0.8.0-dev.1", "dev", "2026-09-10T10:00:00Z")));
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.Equal(ReleaseChannel.Stable, viewModel.SelectedReleaseChannel.Channel);

        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Dev);
        await viewModel.WhenReleaseChannelSavedAsync();

        Assert.Equal(ReleaseChannel.Dev, viewModel.SelectedReleaseChannel.Channel);
        Assert.Equal(ReleaseChannel.Dev, harness.Services.Settings.ReleaseChannel);
        Assert.Equal(ReleaseChannel.Dev, (await harness.Services.SettingsRepository.GetAsync())!.ReleaseChannel);
        Assert.Null(viewModel.PreferenceSaveError);

        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        await afc.InstallCommand.ExecuteAsync(null);

        Assert.Equal(ModVersion.Parse("0.8.0-dev.1"), Assert.Single(afc.PendingPlan!.Operations).Release.Version);
    }

    [Fact]
    public async Task ChannelChange_FailedSave_ShowsTheErrorAndKeepsTheSavedChannel()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await File.WriteAllTextAsync(harness.Services.Paths.GetBoreaSettingsPath(), "not = [toml");

        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Dev);
        await viewModel.WhenReleaseChannelSavedAsync();

        Assert.NotNull(viewModel.PreferenceSaveError);
        Assert.Equal(ReleaseChannel.Stable, viewModel.SelectedReleaseChannel.Channel);
        Assert.False(viewModel.IsSetupBusy);
    }

    [Fact]
    public async Task ChannelChange_WhileSetupOrAnotherSaveRuns_IsRefused()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var raised = 0;
        viewModel.PropertyChanged += (_, e) => raised += e.PropertyName == nameof(MainViewModel.SelectedReleaseChannel) ? 1 : 0;

        viewModel.IsSetupBusy = true;
        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Dev);
        viewModel.IsSetupBusy = false;

        Assert.Equal(1, raised);
        Assert.Equal(ReleaseChannel.Stable, (await harness.Services.SettingsRepository.GetAsync())?.ReleaseChannel ?? ReleaseChannel.Stable);

        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Dev);
        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Stable);
        await viewModel.WhenReleaseChannelSavedAsync();

        Assert.Equal(ReleaseChannel.Dev, harness.Services.Settings.ReleaseChannel);
        Assert.Equal(ReleaseChannel.Dev, viewModel.SelectedReleaseChannel.Channel);
    }

    [Fact]
    public async Task VersionFilter_HidesEveryRelease_SaysSoInsteadOfNoReleases()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json =>
        {
            var root = JsonNode.Parse(json)!;
            var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == "AdvancedFlightComputer")!;
            foreach (var release in listing["releases"]!.AsArray())
                release!["release_status"] = "dev";
            return root.ToJsonString();
        });
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        Assert.Equal(harness.Localization.ContentNoVersions, viewModel.ContentVersionsEmptyText);

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.ContentVersions);
        Assert.Equal(harness.Localization.ContentNoVersionsInChannel, viewModel.ContentVersionsEmptyText);
    }

    [Fact]
    public async Task VersionFilter_StartsAtTheSavedChannelAndHidesReleasesOutsideIt()
    {
        using var harness = await ViewModelHarness.CreateAsync(
            seed: services => services.SettingsRepository.SaveAsync(new BoreaSettings(null, releaseChannel: ReleaseChannel.Testing)),
            editSnapshot: SnapshotRelease.Add(
                new SnapshotRelease("AdvancedFlightComputer", "0.8.0-beta.1", "testing", "2026-09-09T10:00:00Z"),
                new SnapshotRelease("AdvancedFlightComputer", "0.9.0-dev.1", "dev", "2026-09-10T10:00:00Z")));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        Assert.Equal(ReleaseChannel.Testing, viewModel.VersionFilter?.Channel);
        Assert.Equal(5, viewModel.ContentVersions.Count);
        Assert.Equal("0.8.0-beta.1", viewModel.ContentVersions[0].Version);
        Assert.DoesNotContain(viewModel.ContentVersions, version => version.IsDev);

        viewModel.SelectVersionFilterCommand.Execute(ReleaseChannel.Dev);
        Assert.Equal(6, viewModel.ContentVersions.Count);

        viewModel.SelectVersionFilterCommand.Execute(ReleaseChannel.Stable);
        Assert.Equal(4, viewModel.ContentVersions.Count);
        Assert.All(viewModel.ContentVersions, version => Assert.Equal(ReleaseStatus.Stable, version.Status));
    }

    [Fact]
    public async Task HomeAndCompatibility_FollowTheChannel()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: SnapshotRelease.Add(
            new SnapshotRelease("KSArmory", "0.9.0-dev.1", "dev", "2026-09-12T10:00:00Z", GameMin: "2026.9.4.5400", GameMinRevision: 5400)));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.True(GameVersion.TryParse("2026.8.22.5348", out var installed));

        await viewModel.RefreshCompatibilityAsync(installed);

        Assert.Equal(["MeasureTools", "AdvancedFlightComputer", "KSArmory"], viewModel.RecentItems.Select(item => item.ModId));
        Assert.True(viewModel.DiscoverItems.Single(item => item.ModId == "KSArmory").IsCompatible);

        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Dev);
        await viewModel.WhenReleaseChannelSavedAsync();
        await viewModel.RefreshCompatibilityAsync(installed);

        Assert.Equal("KSArmory", viewModel.RecentItems[0].ModId);
        Assert.True(viewModel.DiscoverItems.Single(item => item.ModId == "KSArmory").IsIncompatible);
    }

    [Fact]
    public async Task InstallOutsideTheChannel_WarnsInTheDisplayLanguage()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: SnapshotRelease.Add(new SnapshotRelease("AdvancedFlightComputer", "0.8.0-dev.1", "dev", "2026-09-10T10:00:00Z")));
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);
        viewModel.VersionFilter = viewModel.OptionFor(ReleaseChannel.Dev);
        harness.Localization.TrySetCulture("de");

        var dev = viewModel.ContentVersions.Single(version => version.IsDev);
        await dev.InstallCommand.ExecuteAsync(null);

        Assert.Contains($"Version 0.8.0-dev.1 hat den Status {harness.Localization.ReleaseDev}, den dein Release-Kanal nicht anbietet.", dev.InstallWarning);
        Assert.DoesNotContain("channel does not offer", dev.InstallWarning);
    }
}

/// <summary>Adds a copy of a listing's first release to the index snapshot, with another version and status.</summary>
internal sealed record SnapshotRelease(string ModId, string Version, string Status, string Date, string? GameMin = null, int? GameMinRevision = null)
{
    public static Func<string, string> Add(params SnapshotRelease[] releases) => json =>
    {
        var root = JsonNode.Parse(json)!;
        foreach (var release in releases)
        {
            var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == release.ModId)!;
            var list = listing["releases"]!.AsArray();
            var copy = list[0]!.DeepClone();
            copy["version"] = release.Version;
            copy["release_status"] = release.Status;
            copy["release_date"] = release.Date;
            if (release.GameMin is not null)
                copy["game_min"] = release.GameMin;
            if (release.GameMinRevision is not null)
                copy["game_min_revision"] = release.GameMinRevision;
            list.Add(copy);
        }

        return root.ToJsonString();
    };
}
