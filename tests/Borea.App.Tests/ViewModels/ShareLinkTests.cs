using Borea.App.ViewModels;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class ShareLinkTests
{
    [Fact]
    public void For_IndexListing_IsTheModPageInTheAuthoredCasing()
    {
        Assert.Equal("https://ksamodding.github.io/Borea/mod/AdvancedFlightComputer/", ShareLinks.For(Listing("AdvancedFlightComputer", "index")));
        Assert.Equal("https://ksamodding.github.io/Borea/mod/StarMap/", ShareLinks.For(Listing("StarMap", "index", ContentType.ModLoader)));
    }

    [Fact]
    public void For_ListingFromAnotherSource_IsNull()
        => Assert.Null(ShareLinks.For(Listing("5000", "spacedock")));

    [Fact]
    public void For_IndexPack_IsThePackPage()
    {
        Assert.Equal("https://ksamodding.github.io/Borea/pack/NavigationStarterPack/", ShareLinks.For(Pack("NavigationStarterPack", "index")));
        Assert.Null(ShareLinks.For(Pack("NavigationStarterPack", "local")));
    }

    [Fact]
    public async Task ContentPage_OfIndexListing_CopiesItsShareLink()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var window = new ClipboardWindow();
        viewModel.WindowServices = window;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        Assert.Equal("https://ksamodding.github.io/Borea/mod/AdvancedFlightComputer/", viewModel.ContentShareUrl);

        await viewModel.CopyContentShareLinkCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.ContentShareUrl, window.CopiedText);
        var toast = viewModel.Toasts.Items[^1];
        Assert.True(toast.IsFinished);
        Assert.Equal(harness.Localization.ContentLinkCopied, toast.Message);
    }

    [Fact]
    public async Task ContentPage_OfListingNotFromTheIndex_HasNoShareLink()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var window = new ClipboardWindow();
        viewModel.WindowServices = window;

        await viewModel.OpenContentCommand.ExecuteAsync(new DiscoverItem(viewModel, Listing("5000", "spacedock")));
        Assert.Null(viewModel.ContentShareUrl);

        await viewModel.CopyContentShareLinkCommand.ExecuteAsync(null);

        Assert.Null(window.CopiedText);
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Fact]
    public async Task PackPage_CopiesItsShareLink()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var window = new ClipboardWindow();
        viewModel.WindowServices = window;

        await viewModel.OpenPackCommand.ExecuteAsync(new PackItem(viewModel, Pack("NavigationStarterPack", "index")));
        await viewModel.CopyPackShareLinkCommand.ExecuteAsync(null);

        Assert.Equal("https://ksamodding.github.io/Borea/pack/NavigationStarterPack/", viewModel.PackShareUrl);
        Assert.Equal(viewModel.PackShareUrl, window.CopiedText);
        Assert.Equal(harness.Localization.ContentLinkCopied, viewModel.Toasts.Items[^1].Message);
    }

    [Fact]
    public async Task CopyLink_WhenTheClipboardFails_ShowsAnError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.WindowServices = new ClipboardWindow { Failure = new InvalidOperationException("The clipboard is busy.") };
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        await viewModel.CopyContentShareLinkCommand.ExecuteAsync(null);

        var toast = viewModel.Toasts.Items[^1];
        Assert.True(toast.IsFailed);
        Assert.Equal(harness.Localization.FormatToastCopyFailed(viewModel.ContentShareUrl!), toast.Message);
    }

    private static ModMetadata Listing(string id, string source, ContentType type = ContentType.Mod) => new(
        specVersion: 1,
        modId: id,
        source: source,
        name: id,
        authors: ["Someone"],
        abstractText: id + " abstract",
        license: "MIT",
        links: new Dictionary<string, string> { ["forums"] = "https://forums.example/" + id },
        gameMin: "2026.1.1.1",
        type: type);

    private static ModPackMetadata Pack(string id, string source) => new(
        specVersion: 1,
        modPackId: id,
        source: source,
        name: "Navigation Starter Pack",
        authors: ["Maxi"],
        abstractText: "Everything you need for maneuver planning.",
        license: "CC0-1.0",
        links: new Dictionary<string, string> { ["forums"] = "https://forums.example/pack" },
        gameMin: "2026.8.3.5117",
        version: ModVersion.Parse("1.0.0"),
        releasedAt: new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero),
        mods: [new ModPackEntry("AdvancedFlightComputer", ModVersion.Parse("0.7.5"))]);

    private sealed class ClipboardWindow : IWindowServices
    {
        public string? CopiedText { get; private set; }

        public Exception? Failure { get; init; }

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string fileTypeName, string text) => Task.FromResult<string?>(null);

        public Task<PickedTextFile?> OpenTextFileAsync(string title, string fileTypeName) => Task.FromResult<PickedTextFile?>(null);

        public Task CopyTextAsync(string text)
        {
            if (Failure is not null)
                return Task.FromException(Failure);
            CopiedText = text;
            return Task.CompletedTask;
        }
    }
}
