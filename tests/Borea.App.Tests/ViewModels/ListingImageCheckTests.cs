using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Borea.App.ViewModels;
using Borea.Core.Listings;

namespace Borea.App.Tests.ViewModels;

/// <summary>A listed StarMap with an icon, whose image the test serves through the harness.</summary>
public sealed class ListingImageCheckTests
{
    private const string ListedUrl = "https://raw.githubusercontent.com/KSAModding/content-index/main/listings/StarMap.toml";
    private const string IconUrl = "https://images.example/starmap.png";
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string BranchIconUrl = "https://raw.githubusercontent.com/alice/StarMap/refs/heads/main/icon.png";
    private const string BranchApi = "https://api.github.com/repos/alice/StarMap/branches/main";
    private const string CommitIconUrl = $"https://raw.githubusercontent.com/alice/StarMap/{Commit}/icon.png";

    [Fact]
    public async Task LoadListed_ImageThatMatchesItsRecord_ShowsNothingNew()
    {
        var bytes = ListingEditorTests.Png(512, 512);
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(Listed(IconUrl, Facts(bytes)), (IconUrl, bytes)));

        var icon = await LoadAsync(harness);

        Assert.False(icon.HasChange);
        Assert.False(icon.HasCheckProblem);
        Assert.False(icon.HasBranchNote);
        Assert.Single(harness.Requests, uri => uri.AbsoluteUri == IconUrl);
    }

    [Fact]
    public async Task LoadListed_ImageWhoseBytesChanged_SaysWhatChangedAndUsesTheNewImage()
    {
        var bytes = ListingEditorTests.Png(512, 512);
        var served = ListingImageMeasurement.Of(bytes, ListingImageRole.Icon);
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(Listed(IconUrl, (new string('a', 64), 1024, 1024, 99999)), (IconUrl, bytes)));
        var editor = harness.ViewModel.ListingEditor;

        var icon = await LoadAsync(harness);

        var changes = string.Join(", ",
            harness.Localization.FormatListingImageChangedPixels("1024", "1024", "512", "512"),
            harness.Localization.FormatListingImageChangedBytes(99999.ToString("N0", CultureInfo.CurrentCulture), served.Size.ToString("N0", CultureInfo.CurrentCulture)));
        Assert.Equal(harness.Localization.FormatListingImageChanged(changes), icon.ChangeText);
        Assert.Contains("width = 1024\n", editor.DocumentText, StringComparison.Ordinal);

        icon.UseNewImageCommand.Execute(null);

        Assert.False(icon.HasChange);
        Assert.Equal((served.Sha256, (long?)512, (long?)512, (long?)served.Size), (icon.Sha256, icon.Width, icon.Height, icon.Size));
        Assert.Contains($"sha256 = \"{served.Sha256}\"\nwidth = 512\nheight = 512\nsize = {served.Size}\n", editor.DocumentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadListed_OnlyTheShaChanged_SaysSo()
    {
        var bytes = ListingEditorTests.Png(512, 512);
        var facts = Facts(bytes) with { Sha256 = new string('b', 64) };
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(Listed(IconUrl, facts), (IconUrl, bytes)));

        var icon = await LoadAsync(harness);

        Assert.Equal(harness.Localization.FormatListingImageChanged(harness.Localization.ListingImageChangedOnlySha), icon.ChangeText);
    }

    [Fact]
    public async Task LoadListed_UnreachableImage_SaysSoAndKeepsTheRecord()
    {
        var facts = Facts(ListingEditorTests.Png(512, 512));
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(Listed(IconUrl, facts)));
        var editor = harness.ViewModel.ListingEditor;

        var icon = await LoadAsync(harness);

        Assert.Contains(IconUrl, icon.CheckProblemText, StringComparison.Ordinal);
        Assert.False(icon.HasChange);
        Assert.Equal(facts.Sha256, icon.Sha256);
        Assert.DoesNotContain(editor.Errors, issue => issue.Location.StartsWith("images", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadListed_BranchAddress_ShowsTheNoteAndUsesTheCommitAddressOfTheSameFile()
    {
        var bytes = ListingEditorTests.Png(512, 512);
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(
            Listed(BranchIconUrl, Facts(bytes)),
            (BranchIconUrl, bytes),
            (CommitIconUrl, bytes),
            (BranchApi, System.Text.Encoding.UTF8.GetBytes($$$"""{"name":"main","commit":{"sha":"{{{Commit}}}"}}"""))));
        var editor = harness.ViewModel.ListingEditor;

        var icon = await LoadAsync(harness);

        Assert.False(icon.HasChange);
        Assert.Equal(harness.Localization.FormatListingImageBranch("main"), icon.BranchNote);
        Assert.Equal(CommitIconUrl, icon.CommitUrl);
        Assert.Equal(BranchIconUrl, icon.Url);
        Assert.Equal([BranchApi, CommitIconUrl], harness.Requests.Select(uri => uri.AbsoluteUri).Where(url => url.Contains("/alice/StarMap/", StringComparison.Ordinal)));

        icon.UseCommitAddressCommand.Execute(null);

        Assert.Equal(CommitIconUrl, icon.Url);
        Assert.False(icon.HasBranchNote);
        Assert.Contains($"url = \"{CommitIconUrl}\"\n", editor.DocumentText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, $"{IconUrl} answered HTTP 404, so the image is not there")]
    [InlineData(false, "8 by 8 pixels is outside the limits")]
    public async Task LoadListed_ImageTheChecksWouldReject_SaysSoAsAProblemWithoutAnError(bool missing, string problem)
    {
        var facts = Facts(ListingEditorTests.Png(512, 512));
        var served = missing ? null : ListingEditorTests.Png(8, 8);
        using var harness = await ViewModelHarness.CreateAsync(respond: request => request.RequestUri!.AbsoluteUri switch
        {
            ListedUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Listed(IconUrl, facts)) },
            IconUrl => served is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(served) },
            _ => null,
        });
        var editor = harness.ViewModel.ListingEditor;

        var icon = await LoadAsync(harness);

        Assert.True(icon.HasNoLongerFits);
        Assert.Contains(problem, icon.NoLongerFitsText, StringComparison.Ordinal);
        Assert.False(icon.HasCheckProblem);
        Assert.False(icon.HasChange);
        Assert.Equal(facts.Sha256, icon.Sha256);
        Assert.DoesNotContain(editor.Errors, issue => issue.Location.StartsWith("images", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartOver_DuringTheCheck_StopsTheReadsAndReopeningReadsNothing()
    {
        var bytes = ListingEditorTests.Png(512, 512);
        var gate = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new ConcurrentQueue<string>();
        using var harness = await ViewModelHarness.CreateAsync(respond: request => request.RequestUri!.AbsoluteUri switch
        {
            ListedUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Listed(IconUrl, Facts(bytes))) },
            IconUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new GatedStream(gate.Task, cancelled, IconUrl)) },
            _ => null,
        });
        var editor = harness.ViewModel.ListingEditor;

        await StartLoadAsync(harness);
        var check = editor.ImageCheck;
        await ViewModelHarness.WaitUntilAsync(() => ImageReads(harness, IconUrl) == 1);
        editor.StartOverCommand.Execute(null);
        await check;
        harness.ViewModel.SetMainWindowHome();
        await harness.ViewModel.OpenListingAsync();
        await editor.ImageCheck;

        Assert.Equal([IconUrl], cancelled);
        Assert.Equal(1, ImageReads(harness, IconUrl));
        Assert.False(editor.Icon!.HasChange);
    }

    [Fact]
    public async Task Leave_DuringTheCheck_ReopeningReadsOnlyTheUnfinishedImage()
    {
        const string OtherUrl = "https://images.example/hero.png";
        var bytes = ListingEditorTests.Png(512, 512);
        var hero = ListingEditorTests.Png(640, 360);
        var gate = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new ConcurrentQueue<string>();
        var listed = Listed(IconUrl, Facts(bytes)) + $"""

            [[images.description]]
            id = "hero"
            url = "{OtherUrl}"
            sha256 = "{new string('c', 64)}"
            width = 640
            height = 360
            size = 1

            """;
        using var harness = await ViewModelHarness.CreateAsync(respond: request => request.RequestUri!.AbsoluteUri switch
        {
            ListedUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listed) },
            IconUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) },
            OtherUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new GatedStream(gate.Task, cancelled, OtherUrl)) },
            _ => null,
        });
        var editor = harness.ViewModel.ListingEditor;

        await StartLoadAsync(harness);
        var check = editor.ImageCheck;
        await ViewModelHarness.WaitUntilAsync(() => ImageReads(harness, OtherUrl) == 1 && !editor.Icon!.AwaitsCheck);
        harness.ViewModel.SetMainWindowHome();
        await check;
        await harness.ViewModel.OpenListingAsync();
        gate.SetResult(hero);
        await editor.ImageCheck;

        Assert.Equal([OtherUrl], cancelled);
        Assert.Equal((1, 2), (ImageReads(harness, IconUrl), ImageReads(harness, OtherUrl)));
        Assert.True(Assert.Single(editor.DescriptionImages).HasChange);
    }

    [Fact]
    public async Task DraftThatIsNotListed_ReadsNoImage()
    {
        var facts = Facts(ListingEditorTests.Png(512, 512));
        using var harness = await ViewModelHarness.CreateAsync(respond: _ => null);
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        editor.Load(new ListingDraft
        {
            Id = "my-mod",
            Icon = new ListingImageRecord(IconUrl) { Sha256 = facts.Sha256, Width = facts.Width, Height = facts.Height, Size = facts.Size },
        });
        harness.ViewModel.SetMainWindowHome();
        await harness.ViewModel.OpenListingAsync();
        await editor.ImageCheck;

        Assert.True(editor.Icon!.IsMeasured);
        Assert.Equal(0, ImageReads(harness, IconUrl));
    }

    private static async Task<ListingImageRow> LoadAsync(ViewModelHarness harness)
    {
        await StartLoadAsync(harness);
        await harness.ViewModel.ListingEditor.ImageCheck;
        return harness.ViewModel.ListingEditor.Icon!;
    }

    private static async Task StartLoadAsync(ViewModelHarness harness)
    {
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.ListedQuery = "starm";
        await editor.LoadListedCommand.ExecuteAsync(null);
    }

    private static int ImageReads(ViewModelHarness harness, string url) => harness.Requests.Count(uri => uri.AbsoluteUri == url);

    private static (string Sha256, long Width, long Height, long Size) Facts(byte[] bytes)
    {
        var measurement = ListingImageMeasurement.Of(bytes, ListingImageRole.Icon);
        return (measurement.Sha256!, measurement.Width, measurement.Height, measurement.Size);
    }

    private static string Listed(string url, (string Sha256, long Width, long Height, long Size) facts) =>
        ListingEditorTests.StarMapListing + $"""

            [images.icon]
            url = "{url}"
            sha256 = "{facts.Sha256}"
            width = {facts.Width}
            height = {facts.Height}
            size = {facts.Size}

            """;

    private static Func<HttpRequestMessage, HttpResponseMessage?> Serve(string listed, params (string Url, byte[] Bytes)[] files) => request =>
    {
        var url = request.RequestUri!.AbsoluteUri;
        if (url == ListedUrl)
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listed) };

        return files.FirstOrDefault(file => file.Url == url) is { Bytes: { } bytes }
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : null;
    };

    /// <summary>A response body that waits for the gate, so a read stays open without work until the test releases or cancels it.</summary>
    private sealed class GatedStream(Task<byte[]> gate, ConcurrentQueue<string> cancelled, string name) : Stream
    {
        private byte[]? _bytes;
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                _bytes ??= await gate.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled.Enqueue(name);
                throw;
            }

            var count = Math.Min(buffer.Length, _bytes.Length - _position);
            _bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
