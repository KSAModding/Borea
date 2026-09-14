using System.Text.Json.Nodes;
using Borea.Core.Mods;
using Borea.Core.Settings;

namespace Borea.App.Tests.ViewModels;

public sealed class ReleaseChannelViewModelTests
{
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

        viewModel.VersionFilter = viewModel.OptionFor(ReleaseChannel.Dev);
        Assert.Equal(6, viewModel.ContentVersions.Count);

        viewModel.VersionFilter = viewModel.OptionFor(ReleaseChannel.Stable);
        Assert.Equal(4, viewModel.ContentVersions.Count);
        Assert.All(viewModel.ContentVersions, version => Assert.Equal(ReleaseStatus.Stable, version.Status));
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
