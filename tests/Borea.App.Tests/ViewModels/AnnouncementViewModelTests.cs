using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Borea.App.ViewModels;
using Borea.Composition;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

public sealed class AnnouncementViewModelTests
{
    private const string FeedUrl = "https://raw.githubusercontent.com/KSAModding/Borea/main/announcements.toml";

    private static readonly DateTimeOffset FirstStart = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static string Post(string id, string date, string title = "Testers wanted", string body = "Try the **beta** of the next release.", string? link = null) => $"""

        [[posts]]
        id = "{id}"
        title = "{title}"
        date = {date}
        body = "{body}"
        {(link is null ? "" : $"link = \"{link}\"")}
        """;

    private static string Feed(params string[] posts) => "spec_version = 1\n" + string.Concat(posts);

    private static Func<HttpRequestMessage, HttpResponseMessage?> Serves(string feed, ConcurrentQueue<HttpRequestMessage>? requests = null) => request =>
    {
        if (request.RequestUri?.AbsoluteUri != FeedUrl)
            return null;

        requests?.Enqueue(request);
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(feed, Encoding.UTF8, "text/plain") };
        response.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
        return response;
    };

    private static Func<BoreaServices, Task> Started(Func<AppPreferences, AppPreferences>? change = null, string? cachedFeed = null) => async services =>
    {
        var preferences = AppPreferences.Empty.WithFirstStartedAt(FirstStart);
        await services.AppPreferences.SaveAsync(change?.Invoke(preferences) ?? preferences, MainViewModel.BundledThemeNames);
        if (cachedFeed is not null)
        {
            await File.WriteAllTextAsync(services.Paths.GetAnnouncementsPath(), cachedFeed);
            await File.WriteAllTextAsync(services.Paths.GetAnnouncementsPath() + ".etag", "\"v1\"");
        }
    };

    private static async Task<ViewModelHarness> LoadAsync(Func<BoreaServices, Task>? seed, Func<HttpRequestMessage, HttpResponseMessage?>? respond)
    {
        var harness = await ViewModelHarness.CreateAsync(seed, respond);
        await harness.ViewModel.WhenAnnouncementsCheckedAsync();
        return harness;
    }

    [Fact]
    public async Task Load_NewPost_ShowsTheBanner()
    {
        using var harness = await LoadAsync(Started(), Serves(Feed(Post("testers", "2026-09-10", link: "https://github.com/KSAModding/Borea"))));
        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowAnnouncementBanner);
        Assert.Equal("Testers wanted", viewModel.CurrentAnnouncement!.Title);
        Assert.Equal("Try the beta of the next release.", viewModel.AnnouncementSummary);
        Assert.Equal(new DateTime(2026, 9, 10).ToString("d", CultureInfo.CurrentCulture), viewModel.AnnouncementDateText);
        Assert.Single(harness.Requests, uri => uri.AbsoluteUri == FeedUrl);
        Assert.True(File.Exists(harness.Services.Paths.GetAnnouncementsPath()));
    }

    [Fact]
    public async Task Load_OffsetDateTime_ShowsTheDateTheAuthorWrote()
    {
        using var harness = await LoadAsync(Started(), Serves(Feed(Post("late", "2026-09-10T23:30:00-05:00"))));

        Assert.Equal(new DateTime(2026, 9, 10).ToString("d", CultureInfo.CurrentCulture), harness.ViewModel.AnnouncementDateText);
    }

    [Fact]
    public async Task Load_PostOlderThanTheFirstStart_ShowsNothing()
    {
        using var harness = await LoadAsync(Started(), Serves(Feed(Post("old", "2026-08-31"), Post("same-day", "2026-09-01T11:59:00Z"))));

        Assert.False(harness.ViewModel.ShowAnnouncementBanner);
        Assert.Null(harness.ViewModel.CurrentAnnouncement);
    }

    [Fact]
    public async Task Load_SeveralPosts_ShowsOnlyTheNewestOne()
    {
        using var harness = await LoadAsync(Started(), Serves(Feed(Post("first", "2026-09-05", title: "First"), Post("newest", "2026-09-12", title: "Newest"), Post("second", "2026-09-08", title: "Second"))));

        Assert.Equal("newest", harness.ViewModel.CurrentAnnouncement!.Id);
    }

    [Fact]
    public async Task Load_FirstStart_RecordsTheTimeAndKeepsIt()
    {
        var before = DateTimeOffset.UtcNow;
        using var harness = await LoadAsync(seed: null, Serves(Feed(Post("past", "2026-09-10"))));
        await harness.ViewModel.WhenPreferencesSavedAsync();

        var saved = (await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames)).Preferences.FirstStartedAt;
        Assert.NotNull(saved);
        Assert.InRange(saved.Value, before, DateTimeOffset.UtcNow);
        Assert.False(harness.ViewModel.ShowAnnouncementBanner);

        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.WhenPreferencesSavedAsync();

        Assert.Equal(saved, (await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames)).Preferences.FirstStartedAt);
    }

    [Fact]
    public async Task Load_InvalidPreferencesFile_LeavesTheFileAsItIs()
    {
        const string broken = "{ not json";
        using var harness = await LoadAsync(services => File.WriteAllTextAsync(services.Paths.GetAppPreferencesPath(), broken), Serves(Feed()));
        await harness.ViewModel.WhenPreferencesSavedAsync();

        Assert.Equal(broken, await File.ReadAllTextAsync(harness.Services.Paths.GetAppPreferencesPath()));
    }

    [Fact]
    public async Task Load_KnownFirstStart_KeepsIt()
    {
        using var harness = await LoadAsync(Started(), Serves(Feed()));
        await harness.ViewModel.WhenPreferencesSavedAsync();

        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.Equal(FirstStart, saved.Preferences.FirstStartedAt);
    }

    [Fact]
    public async Task Close_HidesThePostForGoodAndShowsTheNextOne()
    {
        var feed = Serves(Feed(Post("older", "2026-09-05"), Post("newer", "2026-09-10")));
        using var harness = await LoadAsync(Started(), feed);
        var viewModel = harness.ViewModel;

        viewModel.DismissAnnouncementCommand.Execute(null);
        await viewModel.WhenPreferencesSavedAsync();

        Assert.Equal("older", viewModel.CurrentAnnouncement!.Id);
        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.Equal(["newer"], saved.Preferences.DismissedAnnouncements);

        viewModel.DismissAnnouncementCommand.Execute(null);
        await viewModel.WhenPreferencesSavedAsync();

        Assert.False(viewModel.ShowAnnouncementBanner);
        saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.Equal(["older", "newer"], saved.Preferences.DismissedAnnouncements);

        using var restarted = await LoadAsync(Started(preferences => preferences.WithDismissedAnnouncements(["older", "newer"])), feed);
        Assert.False(restarted.ViewModel.ShowAnnouncementBanner);
    }

    [Fact]
    public async Task Close_FullList_DropsTheOldestPost()
    {
        var ids = Enumerable.Range(0, 51).Select(minute => $"post-{minute:00}").ToList();
        var feed = Feed(ids.Select((id, minute) => Post(id, $"2026-09-10T00:{minute:00}:00Z")).ToArray());
        var dismissed = ids.Where(id => id != "post-25").ToList();
        using var harness = await LoadAsync(Started(preferences => preferences.WithDismissedAnnouncements(dismissed)), Serves(feed));
        Assert.Equal("post-25", harness.ViewModel.CurrentAnnouncement!.Id);

        harness.ViewModel.DismissAnnouncementCommand.Execute(null);
        await harness.ViewModel.WhenPreferencesSavedAsync();

        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.Equal(ids.Skip(1), saved.Preferences.DismissedAnnouncements);
    }

    [Fact]
    public async Task SeeMore_OpensThePostAndClosesAgain()
    {
        using var harness = await LoadAsync(Started(), Serves(Feed(Post("testers", "2026-09-10", link: "https://github.com/KSAModding/Borea"))));
        var viewModel = harness.ViewModel;

        viewModel.OpenAnnouncementCommand.Execute(null);

        Assert.True(viewModel.IsAnnouncementOpen);
        Assert.Equal("Try the **beta** of the next release.", viewModel.CurrentAnnouncement!.Body);
        Assert.Equal("https://github.com/KSAModding/Borea", viewModel.CurrentAnnouncement.Link);

        viewModel.CloseAnnouncementCommand.Execute(null);

        Assert.False(viewModel.IsAnnouncementOpen);
        Assert.True(viewModel.ShowAnnouncementBanner);
    }

    [Fact]
    public async Task Load_EtagMatches_ShowsTheCachedPost()
    {
        var requests = new ConcurrentQueue<HttpRequestMessage>();
        using var harness = await LoadAsync(
            Started(cachedFeed: Feed(Post("cached", "2026-09-10"))),
            request =>
            {
                if (request.RequestUri?.AbsoluteUri != FeedUrl)
                    return null;

                requests.Enqueue(request);
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            });

        Assert.Equal("cached", harness.ViewModel.CurrentAnnouncement!.Id);
        Assert.Contains(Assert.Single(requests).Headers.IfNoneMatch, tag => tag.Tag == "\"v1\"");
    }

    [Fact]
    public async Task Load_Offline_ShowsTheCachedPost()
    {
        using var harness = await LoadAsync(Started(cachedFeed: Feed(Post("cached", "2026-09-10"))), respond: null);

        Assert.Equal("cached", harness.ViewModel.CurrentAnnouncement!.Id);
        Assert.Single(harness.Requests, uri => uri.AbsoluteUri == FeedUrl);
    }

    [Fact]
    public async Task Load_BrokenPost_IsSkipped()
    {
        using var harness = await LoadAsync(Started(), Serves(Feed(Post("good", "2026-09-05"), Post("bad id", "2026-09-12"))));

        Assert.Equal("good", harness.ViewModel.CurrentAnnouncement!.Id);
        Assert.Contains(harness.Services.Log.ReadRecentLines(50), line => line.Contains("Announcements: skipped a post", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Load_BrokenFile_KeepsTheCache()
    {
        var cached = Feed(Post("cached", "2026-09-10"));
        using var harness = await LoadAsync(Started(cachedFeed: cached), Serves("spec_version = [ broken"));

        Assert.Equal("cached", harness.ViewModel.CurrentAnnouncement!.Id);
        Assert.Equal(cached, await File.ReadAllTextAsync(harness.Services.Paths.GetAnnouncementsPath()));
        Assert.Equal("\"v1\"", await File.ReadAllTextAsync(harness.Services.Paths.GetAnnouncementsPath() + ".etag"));
    }

    [Fact]
    public async Task Load_SwitchOff_SendsNoRequestAndShowsNothing()
    {
        using var harness = await LoadAsync(
            Started(preferences => preferences.WithFetchAnnouncements(false), cachedFeed: Feed(Post("cached", "2026-09-10"))),
            Serves(Feed(Post("new", "2026-09-12"))));

        Assert.False(harness.ViewModel.FetchAnnouncements);
        Assert.False(harness.ViewModel.ShowAnnouncementBanner);
        Assert.NotEmpty(harness.Requests);
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsoluteUri == FeedUrl);
    }

    [Fact]
    public async Task TurnSwitchOff_HidesTheBannerAndSavesThePreference()
    {
        using var harness = await LoadAsync(Started(), Serves(Feed(Post("new", "2026-09-12"))));
        var viewModel = harness.ViewModel;
        Assert.True(viewModel.ShowAnnouncementBanner);

        viewModel.FetchAnnouncements = false;
        await viewModel.WhenPreferencesSavedAsync();

        Assert.False(viewModel.ShowAnnouncementBanner);
        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.False(saved.Preferences.FetchAnnouncements);
    }

    [Fact]
    public async Task TurnSwitchOff_DuringTheFetch_CancelsItAndWritesNoCache()
    {
        using var harness = await ViewModelHarness.CreateAsync(Started(), request => request.RequestUri?.AbsoluteUri == FeedUrl
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream()) }
            : null);
        var viewModel = harness.ViewModel;

        viewModel.FetchAnnouncements = false;
        await viewModel.WhenAnnouncementsCheckedAsync();

        Assert.False(viewModel.ShowAnnouncementBanner);
        Assert.False(File.Exists(harness.Services.Paths.GetAnnouncementsPath()));
        Assert.False(File.Exists(harness.Services.Paths.GetAnnouncementsPath() + ".etag"));
    }

    [Fact]
    public async Task TurnSwitchOn_FetchesAndShowsTheNewPost()
    {
        using var harness = await LoadAsync(Started(preferences => preferences.WithFetchAnnouncements(false)), Serves(Feed(Post("new", "2026-09-12"))));
        var viewModel = harness.ViewModel;

        viewModel.FetchAnnouncements = true;
        await viewModel.WhenAnnouncementsCheckedAsync();

        Assert.Equal("new", viewModel.CurrentAnnouncement!.Id);
        Assert.Single(harness.Requests, uri => uri.AbsoluteUri == FeedUrl);
    }

    [Theory]
    [InlineData("## Heading\n\nFirst **bold** [link](https://example.com) paragraph.\n\nSecond.", "First bold link paragraph.")]
    [InlineData("# Only a heading", "Only a heading")]
    [InlineData("```\ncode\n```\nPlain  text\nacross lines.", "Plain text across lines.")]
    [InlineData("", "")]
    public void Summarize_ReadsTheFirstBlockAsPlainText(string markdown, string expected)
    {
        Assert.Equal(expected, MainViewModel.Summarize(markdown));
    }

    [Fact]
    public void Summarize_LongText_CutsAtAWord()
    {
        var summary = MainViewModel.Summarize(string.Join(" ", Enumerable.Repeat("word", 60)));

        Assert.EndsWith("word...", summary, StringComparison.Ordinal);
        Assert.True(summary.Length <= MainViewModel.AnnouncementSummaryLength + 3);
    }

    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
