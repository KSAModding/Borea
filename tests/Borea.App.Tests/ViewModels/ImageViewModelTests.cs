using System.Collections.Specialized;
using Borea.App.ViewModels;
using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class ImageViewModelTests
{
    private const string IconUrl = "https://images.example/afc-icon.png";

    private static readonly string Digest = new('A', 64);

    private static readonly byte[] IconBytes = [1, 2, 3];

    [Fact]
    public async Task Icon_Loaded_ShowsItsBytesOnTheRowTheTileAndTheHeader()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithImages("AdvancedFlightComputer", $$"""{ "icon": {{Icon(IconUrl)}} }"""));
        harness.Images.Respond = _ => ContentImageResult.Loaded(IconBytes);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var row = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        var icon = Assert.IsType<ListingImage>(row.Icon);
        Assert.False(icon.IsLoaded);
        Assert.Empty(harness.Images.Requests);

        await icon.LoadAsync();
        await icon.LoadAsync();

        Assert.True(icon.IsLoaded);
        Assert.Equal(IconBytes, icon.Bytes);
        var request = Assert.Single(harness.Images.Requests);
        Assert.Equal(IconUrl, request.Image.Url);
        Assert.True(request.LoadFromAuthorHosts);
        Assert.Same(icon, viewModel.RecentItems.Single(item => item.ModId == "AdvancedFlightComputer").Icon);
        Assert.Null(viewModel.DiscoverItems.Single(item => item.ModId == "KSArmory").Icon);

        await row.OpenCommand.ExecuteAsync(null);

        Assert.Same(icon, viewModel.SelectedContent?.Icon);
    }

    [Fact]
    public async Task Icon_FailedToLoad_KeepsThePlaceholderAndIsNotRequestedAgain()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithImages("AdvancedFlightComputer", $$"""{ "icon": {{Icon(IconUrl)}} }"""));
        harness.Images.Respond = _ => ContentImageResult.Failed(ContentImageFailure.FactsMismatch, "The sha256 differs.");
        var clock = new ManualClock();
        harness.ViewModel.ImageClock = clock;
        var icon = await DiscoverIconAsync(harness);

        await icon.LoadAsync();
        clock.Advance(ListingImage.UnavailableRetryDelay);
        await icon.LoadAsync();

        Assert.False(icon.IsLoaded);
        Assert.Null(icon.Bytes);
        Assert.Equal(ContentImageFailure.FactsMismatch, icon.Failure);
        Assert.Single(harness.Images.Requests);
    }

    [Fact]
    public async Task Icon_HostUnavailable_IsRequestedAgainAfterTheRetryDelay()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithImages("AdvancedFlightComputer", $$"""{ "icon": {{Icon(IconUrl)}} }"""));
        harness.Images.Respond = _ => ContentImageResult.Failed(ContentImageFailure.Unavailable, "The host timed out.");
        var clock = new ManualClock();
        harness.ViewModel.ImageClock = clock;
        var icon = await DiscoverIconAsync(harness);
        await icon.LoadAsync();

        clock.Advance(ListingImage.UnavailableRetryDelay - TimeSpan.FromSeconds(1));
        await icon.LoadAsync();

        Assert.Single(harness.Images.Requests);

        harness.Images.Respond = _ => ContentImageResult.Loaded(IconBytes);
        clock.Advance(TimeSpan.FromSeconds(1));
        await icon.LoadAsync();

        Assert.True(icon.IsLoaded);
        Assert.Null(icon.Failure);
        Assert.Equal(2, harness.Images.Requests.Count);
    }

    [Fact]
    public async Task Icon_ImagesFromAuthorHostsOff_AsksOnlyTheCacheAndKeepsThePlaceholder()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithImages("AdvancedFlightComputer", $$"""{ "icon": {{Icon(IconUrl)}} }"""));
        harness.Images.Respond = _ => ContentImageResult.Loaded(IconBytes);
        var icon = await DiscoverIconAsync(harness);
        harness.ViewModel.LoadImagesFromAuthorHosts = false;

        await icon.LoadAsync();

        Assert.False(icon.IsLoaded);
        Assert.Equal(ContentImageFailure.DisabledByPreference, icon.Failure);
        Assert.False(Assert.Single(harness.Images.Requests).LoadFromAuthorHosts);
    }

    [Fact]
    public async Task TurningImagesFromAuthorHostsOn_LoadsTheIconItHeldBack()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithImages("AdvancedFlightComputer", $$"""{ "icon": {{Icon(IconUrl)}} }"""));
        harness.Images.Respond = _ => ContentImageResult.Loaded(IconBytes);
        var icon = await DiscoverIconAsync(harness);
        harness.ViewModel.LoadImagesFromAuthorHosts = false;
        await icon.LoadAsync();

        harness.ViewModel.LoadImagesFromAuthorHosts = true;
        await icon.LoadAsync();

        Assert.True(icon.IsLoaded);
        Assert.Null(icon.Failure);
        Assert.Equal([false, true], harness.Images.Requests.Select(request => request.LoadFromAuthorHosts));
    }

    [Fact]
    public async Task DiscoverFilters_ThatKeepTheRows_LeaveTheRowsAndTheirLoadedIconsAlone()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithImages("AdvancedFlightComputer", $$"""{ "icon": {{Icon(IconUrl)}} }"""));
        harness.Images.Respond = _ => ContentImageResult.Loaded(IconBytes);
        var viewModel = harness.ViewModel;
        var icon = await DiscoverIconAsync(harness);
        await icon.LoadAsync();
        var rows = viewModel.DiscoverItems.ToList();
        var changes = new List<NotifyCollectionChangedAction>();
        viewModel.DiscoverItems.CollectionChanged += (_, e) => changes.Add(e.Action);

        viewModel.HideInstalled = true;
        viewModel.HideIncompatible = true;

        Assert.Empty(changes);
        Assert.Equal(rows, viewModel.DiscoverItems);
        Assert.True(icon.IsLoaded);
        Assert.Single(harness.Images.Requests);
    }

    [Fact]
    public async Task DiscoverFilter_RowThatComesBack_IsTheSameRowWithItsLoadedIcon()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithImages("AdvancedFlightComputer", $$"""{ "icon": {{Icon(IconUrl)}} }"""));
        harness.Images.Respond = _ => ContentImageResult.Loaded(IconBytes);
        var viewModel = harness.ViewModel;
        var icon = await DiscoverIconAsync(harness);
        await icon.LoadAsync();
        var rows = viewModel.DiscoverItems.ToList();
        var changes = new List<NotifyCollectionChangedAction>();
        viewModel.DiscoverItems.CollectionChanged += (_, e) => changes.Add(e.Action);

        viewModel.SearchText = "armory";
        viewModel.SearchText = string.Empty;

        Assert.Equal(rows, viewModel.DiscoverItems);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, changes);
        Assert.Same(icon, viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").Icon);
        Assert.True(icon.IsLoaded);
        Assert.Single(harness.Images.Requests);
    }

    [Fact]
    public void IconFor_RecordsThatDifferOnlyInHeight_AreSeparateImages()
    {
        var viewModel = new MainViewModel();
        var wide = viewModel.IconFor(new IconImage(IconUrl, Digest, 1024, 512, 4096));

        Assert.Same(wide, viewModel.IconFor(new IconImage(IconUrl, Digest, 1024, 512, 4096)));
        Assert.NotSame(wide, viewModel.IconFor(new IconImage(IconUrl, Digest, 1024, 640, 4096)));
    }

    [Fact]
    public async Task OpenContent_TakesTheDescriptionImagesOfTheListingWithoutLoadingThem()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithImages("AdvancedFlightComputer", $$"""{ "description": [{{Description("settings-window")}}] }"""));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        Assert.Equal("https://images.example/settings-window.png", viewModel.ContentDescriptionImages.Find("settings-window")?.Record.Url);
        Assert.Empty(harness.Images.Requests);

        viewModel.SetMainWindowDiscover();

        Assert.Same(DescriptionImages.None, viewModel.ContentDescriptionImages);
    }

    [Theory]
    [InlineData(ModInstallOwnership.Borea)]
    [InlineData(ModInstallOwnership.Foreign)]
    public async Task InstanceRow_OfAListedMod_SharesTheIconOfItsListing(ModInstallOwnership ownership)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithImages("AdvancedFlightComputer", $$"""{ "icon": {{Icon(IconUrl)}} }"""));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ownership);
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        var row = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
        Assert.NotNull(row.Icon);
        Assert.Same(viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").Icon, row.Icon);
    }

    [Fact]
    public async Task PackImages_ComeFromTheNewestVersionThatIsNotRetracted()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => json.Replace(
            "\"packs\": []",
            $"\"packs\": [{{ \"id\": \"starter-pack\", \"versions\": [{PackVersion("1.0.0")}, {PackVersion("1.1.0")}, {PackVersion("1.2.0", retracted: true)}] }}]",
            StringComparison.Ordinal));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);

        await pack.OpenCommand.ExecuteAsync(null);

        Assert.Equal("1.1.0", pack.Version);
        Assert.Equal("https://images.example/starter-pack-1.1.0.png", pack.Icon?.Record.Url);
        Assert.Equal("https://images.example/starter-pack-1.1.0-shot.png", viewModel.PackDescriptionImages.Find("shot")?.Record.Url);
        Assert.Empty(harness.Images.Requests);
    }

    private static async Task<ListingImage> DiscoverIconAsync(ViewModelHarness harness)
    {
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return Assert.IsType<ListingImage>(harness.ViewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").Icon);
    }

    private static string Icon(string url) =>
        $$"""{ "url": "{{url}}", "sha256": "{{Digest}}", "width": 512, "height": 512, "size": 4096 }""";

    private static string PackVersion(string version, bool retracted = false)
    {
        var status = retracted ? """ "index_status": { "state": "retracted", "reason": "Broken build." },""" : string.Empty;
        return $$"""
            {{{status}} "authored": { "spec_version": 1, "id": "starter-pack", "type": "modpack", "name": "Starter Pack", "authors": ["Maxi"], "abstract": "Starter Pack abstract.", "license": "MIT", "version": "{{version}}", "released_at": "2026-09-01T12:00:00Z", "links": { "forums": "https://forums.example.com/starter-pack" }, "compatibility": { "game_min": "2026.8.19.5261" }, "mods": [{ "id": "MeasureTools", "version": "1.1.10" }], "images": { "icon": {{Icon($"https://images.example/starter-pack-{version}.png")}}, "description": [{ "id": "shot", "url": "https://images.example/starter-pack-{{version}}-shot.png", "sha256": "{{Digest}}", "width": 1600, "height": 900, "size": 400000 }] } } }
            """;
    }

    private static string Description(string id) =>
        $$"""{ "id": "{{id}}", "url": "https://images.example/{{id}}.png", "sha256": "{{Digest}}", "width": 1600, "height": 900, "size": 400000 }""";

    private static Func<string, string> WithImages(string listingId, string images) => json =>
    {
        const string authored = "\"authored\": {";
        var listing = json.IndexOf($"\"id\": \"{listingId}\",", StringComparison.Ordinal);
        var at = json.IndexOf(authored, listing, StringComparison.Ordinal) + authored.Length;
        return json.Insert(at, $" \"images\": {images},");
    };

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
