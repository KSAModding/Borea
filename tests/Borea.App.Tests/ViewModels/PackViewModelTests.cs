using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Borea.App.ViewModels;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class PackViewModelTests
{
    private const string MeasureToolsUrl = "https://github.com/Maximilian-Nesslauer/KSA-MeasureTools/releases/download/v1.1.10/MeasureTools.zip";
    private const string MeasureToolsSha256 = "8718558358629EFC3753ACFF9052851EFEB142A9343A1794485C177651265F15";

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
        Assert.Equal(["starter"], starter.Tags);
        Assert.Equal(GameCompatibility.Unknown, starter.Compatibility);
        Assert.False(string.IsNullOrWhiteSpace(starter.ReleasedText));
        Assert.Null(starter.PublishedText);
        Assert.StartsWith("Published ", viewModel.DiscoverPacks[0].PublishedText);

        viewModel.SearchText = "armory";
        Assert.Equal(["armory-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));

        viewModel.ShowDiscoverModsCommand.Execute(null);
        Assert.Empty(viewModel.DiscoverPacks);
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
        Assert.Equal(harness.Localization.FormatPackIncomplete(1, 1), pack.InstallError);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
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
    public async Task Install_UntestedDeprecatedPack_WaitsForConfirmation()
    {
        var pack = Pack("armory-pack", "Armory Pack", Version("1.0.0", Pin("KSArmory", "0.8.44")))
            .Replace("\"game_min\": \"2026.8.19.5261\" }", "\"game_min\": \"2026.8.0.1\", \"game_max\": \"2026.8.1.1\" }, \"status\": \"deprecated\", \"superseded_by\": \"new-armory-pack\"", StringComparison.Ordinal);
        using var harness = await CreateWithGameAsync(WithPacks(pack));
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

        Assert.Equal(harness.Localization.FormatPackIncompatible("2026.8.19.5261"), pack.InstallError);
        Assert.Null(pack.InstallWarning);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);

        viewModel.HideIncompatible = true;
        Assert.Empty(viewModel.DiscoverPacks);
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
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return instance;
    }

    private static Func<string, string> WithPacks(params string[] packs) => snapshot =>
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

    private static string Pack(string id, string name, params Func<string, string, string>[] versions) =>
        $$"""{ "id": "{{id}}", "versions": [{{string.Join(", ", versions.Select(version => version(id, name)))}}] }""";

    private static Func<string, string, string> Version(string version, params string[] pins) => (id, name) =>
        $$"""{ "authored": { "spec_version": 1, "id": "{{id}}", "type": "modpack", "name": "{{name}}", "authors": ["Maxi"], "abstract": "{{name}} abstract.", "description": "## {{name}}", "license": "MIT", "tags": ["starter"], "version": "{{version}}", "released_at": "2026-09-01T12:00:00Z", "links": { "forums": "https://forums.example.com/{{id}}" }, "compatibility": { "game_min": "2026.8.19.5261" }, "mods": [{{string.Join(", ", pins)}}] } }""";

    private static string Pin(string id, string version) => $$"""{ "id": "{{id}}", "version": "{{version}}" }""";

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
