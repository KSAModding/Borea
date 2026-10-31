using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Announcements;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Preferences;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Borea.App.ViewModels;

/// <summary>The announcements of the KSAModding team on Home, fetched once per start.</summary>
public partial class MainViewModel
{
    internal const int AnnouncementSummaryLength = 160;

    private Task? _announcementCheck;

    private CancellationTokenSource? _announcementCancellation;

    private bool? _fetchAnnouncements;

    private DateTimeOffset? _firstStartedAt;

    private IReadOnlyList<string>? _dismissedAnnouncements;

    private IReadOnlyList<Announcement> _announcements = [];

    /// <summary>The newest post dated after the first start that the user did not close, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAnnouncementBanner))]
    [NotifyPropertyChangedFor(nameof(AnnouncementSummary))]
    [NotifyPropertyChangedFor(nameof(AnnouncementDateText))]
    private Announcement? _currentAnnouncement;

    public bool ShowAnnouncementBanner => CurrentAnnouncement is not null;

    public string? AnnouncementSummary => CurrentAnnouncement is { } post ? Summarize(post.Body) : null;

    public string? AnnouncementDateText => CurrentAnnouncement is { } post ? post.Date.DateTime.ToString("d", CultureInfo.CurrentCulture) : null;

    [ObservableProperty]
    private bool _isAnnouncementOpen;

    /// <summary>How the App preferences loaded. Only a file that loaded or did not exist gets the first start written.</summary>
    public AppPreferencesLoadStatus PreferencesLoadStatus { get; init; } = AppPreferencesLoadStatus.Loaded;

    [ObservableProperty]
    private string? _announcementLinkError;

    /// <summary>The switch in the General settings. Off hides the banner at once and fetches nothing.</summary>
    public bool FetchAnnouncements
    {
        get => _fetchAnnouncements ?? _appPreferences.FetchAnnouncements;
        set
        {
            if (value == FetchAnnouncements)
                return;

            _fetchAnnouncements = value;
            OnPropertyChanged();
            QueuePreferenceSave(preferences => preferences.WithFetchAnnouncements(value));
            if (value)
            {
                StartAnnouncementCheck();
            }
            else
            {
                _announcementCancellation?.Cancel();
                IsAnnouncementOpen = false;
            }
            UpdateCurrentAnnouncement();
        }
    }

    private DateTimeOffset? FirstStartedAt => _firstStartedAt ?? _appPreferences.FirstStartedAt;

    private IReadOnlyList<string> DismissedAnnouncements => _dismissedAnnouncements ?? _appPreferences.DismissedAnnouncements;

    /// <summary>Records the first start once, so a new install shows no older posts.</summary>
    private void RecordFirstStart()
    {
        if (FirstStartedAt is not null)
            return;

        var now = DateTimeOffset.UtcNow;
        _firstStartedAt = now;
        if (PreferencesLoadStatus is not (AppPreferencesLoadStatus.Loaded or AppPreferencesLoadStatus.NotFound))
            return;

        QueuePreferenceSave(preferences => preferences.FirstStartedAt is null ? preferences.WithFirstStartedAt(now) : preferences);
    }

    private void StartAnnouncementCheck()
    {
        if (_services is not { } services || !FetchAnnouncements || _announcementCancellation is { IsCancellationRequested: false })
            return;

        var cancellation = new CancellationTokenSource();
        _announcementCancellation = cancellation;
        _announcementCheck = CheckAnnouncementsAsync(services, cancellation);
    }

    internal Task WhenAnnouncementsCheckedAsync() => _announcementCheck ?? Task.CompletedTask;

    private async Task CheckAnnouncementsAsync(BoreaServices services, CancellationTokenSource cancellation)
    {
        try
        {
            _announcements = await services.Announcements.GetPostsAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            // a settings change replaced the services while the check ran
            if (_announcementCancellation == cancellation)
                _announcementCancellation = null;
            if (!ReferenceEquals(_services, services))
                StartAnnouncementCheck();
            return;
        }

        UpdateCurrentAnnouncement();
    }

    private void UpdateCurrentAnnouncement()
    {
        var dismissed = new HashSet<string>(DismissedAnnouncements, ModIds.Comparer);
        CurrentAnnouncement = FetchAnnouncements && FirstStartedAt is { } firstStart
            ? _announcements
                .Where(post => post.Date > firstStart && !dismissed.Contains(post.Id))
                .OrderByDescending(post => post.Date)
                .FirstOrDefault()
            : null;
    }

    [RelayCommand]
    private void OpenAnnouncement()
    {
        AnnouncementLinkError = null;
        IsAnnouncementOpen = CurrentAnnouncement is not null;
    }

    [RelayCommand]
    private void CloseAnnouncement() => IsAnnouncementOpen = false;

    [RelayCommand]
    private void OpenAnnouncementLink()
    {
        if (CurrentAnnouncement?.Link is { } link)
            AnnouncementLinkError = TryOpenUrl(link);
    }

    /// <summary>Hides the post for good. The saved ids are ordered by post date, so the cap drops the oldest posts first.</summary>
    [RelayCommand]
    private void DismissAnnouncement()
    {
        if (CurrentAnnouncement is not { } post)
            return;

        var dates = _announcements.ToDictionary(item => item.Id, item => item.Date, ModIds.Comparer);
        var ids = DismissedAnnouncements
            .Where(id => !ModIds.Equals(id, post.Id))
            .Append(post.Id)
            .OrderBy(id => dates.TryGetValue(id, out var date) ? date : DateTimeOffset.MinValue)
            .ToList();
        _dismissedAnnouncements = ids;
        IsAnnouncementOpen = false;
        UpdateCurrentAnnouncement();
        QueuePreferenceSave(preferences => preferences.WithDismissedAnnouncements(ids));
    }

    /// <summary>The first paragraph of a Markdown body as plain text, cut at a word.</summary>
    internal static string Summarize(string markdown)
    {
        var document = MarkdownSyntax.Parse(markdown);
        var block = document.OfType<ParagraphBlock>().FirstOrDefault() ?? document.Descendants<LeafBlock>().FirstOrDefault(item => item.Inline is not null);
        if (block?.Inline is not { } inlines)
            return string.Empty;

        var builder = new StringBuilder();
        AppendSummary(builder, inlines);
        var text = Whitespace().Replace(builder.ToString(), " ").Trim();
        if (text.Length <= AnnouncementSummaryLength)
            return text;

        var cut = text.LastIndexOf(' ', AnnouncementSummaryLength);
        return text[..(cut > 0 ? cut : AnnouncementSummaryLength)].TrimEnd() + "...";
    }

    /// <summary>An image adds nothing to the summary at any depth, and a br separates the words on each side of it.</summary>
    private static void AppendSummary(StringBuilder builder, Inline inline)
    {
        switch (inline)
        {
            case LinkInline { IsImage: true }:
                break;
            case HtmlInline html:
                if (LineBreakTag().IsMatch(html.Tag))
                    builder.Append(' ');
                break;
            case ContainerInline container:
                foreach (var child in container)
                    AppendSummary(builder, child);
                break;
            default:
                builder.Append(MarkdownSyntax.PlainText(inline));
                break;
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^<br\b", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTag();
}
