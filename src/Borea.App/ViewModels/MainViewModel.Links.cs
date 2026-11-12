using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.App.Links;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Links;
using Borea.Core.Logging;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.App.ViewModels;

/// <summary>
/// borea:// links. A link opens a page of the content index, and an install or modlist link only prepares
/// the install, which then waits for a click.
/// </summary>
public partial class MainViewModel
{
    private Task? _startLoad;
    private bool? _openBoreaLinks;
    private bool _linkRegistrationStarted;
    private Task _linkRegistration = Task.CompletedTask;

    /// <summary>Null in tests and for a build that is not a published borea program.</summary>
    internal LinkHandler? LinkHandler { get; init; }

    public bool CanRegisterLinks => LinkHandler is not null;

    /// <summary>The switch in the General settings. Off removes the registration when it points at this Borea.</summary>
    public bool OpenBoreaLinks
    {
        get => _openBoreaLinks ?? _appPreferences.OpenBoreaLinks;
        set
        {
            if (value == OpenBoreaLinks)
                return;

            _openBoreaLinks = value;
            OnPropertyChanged();
            QueuePreferenceSave(preferences => preferences.WithOpenBoreaLinks(value));
            QueueLinkRegistration(value);
        }
    }

    internal Task WhenLinkRegistrationDoneAsync() => _linkRegistration;

    private void StartLinkRegistration()
    {
        if (_linkRegistrationStarted)
            return;

        _linkRegistrationStarted = true;
        QueueLinkWork((handler, log) => handler.AddLauncher(log));
        if (OpenBoreaLinks)
            QueueLinkRegistration(enabled: true);
    }

    private void QueueLinkRegistration(bool enabled) => QueueLinkWork((handler, log) => handler.Apply(enabled, log));

    /// <summary>One queue for both, because the launcher and the handler entry write the same icon file.</summary>
    private void QueueLinkWork(Action<LinkHandler, IBoreaLog?> work)
    {
        if (LinkHandler is not { } handler)
            return;

        var log = _services?.Log;
        _linkRegistration = _linkRegistration.ContinueWith(_ => work(handler, log), TaskScheduler.Default);
    }

    /// <summary>A link with an unescaped quote can arrive split into several arguments, so only a start with one argument opens it.</summary>
    internal async Task OpenStartLinkAsync(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 1)
            await OpenLinkAsync(arguments[0]);
        else
            RefuseLink(string.Join(' ', arguments), "the link came as more than one argument", detail: null);
    }

    /// <summary>Opens what a borea:// link names. A refused link is logged and shown in a toast, and nothing else happens.</summary>
    internal async Task OpenLinkAsync(string text)
    {
        if (!BoreaLink.TryParse(text, out var link, out var refusal, out var problem))
        {
            RefuseLink(text, refusal, ModListProblemText(problem));
            return;
        }

        if (_services is not { } services)
            return;

        services.Log.Write($"Opening the link {link}.");
        try
        {
            await OpenLinkAsync(services, link);
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            ShowErrorToast(() => Localization.LinkRefused, exception.Message);
        }
    }

    /// <param name="detail">What to fix in the link, in the language of the player, or null.</param>
    private void RefuseLink(string text, string reason, string? detail)
    {
        _services?.Log.Write($"Refused the link {BoreaLink.ForLog(text)}, because {reason}.");
        Toasts.ShowMessage(ToastKind.Error, () => Localization.LinkRefused, detail);
    }

    /// <summary>What a server owner fixes in a modlist link that Borea refused, or null for any other refused link.</summary>
    private string? ModListProblemText(ModListLinkProblem problem) => problem switch
    {
        ModListLinkProblem.TooLong => Localization.FormatLinkModListTooLong(BoreaLink.MaxLength),
        ModListLinkProblem.Form => Localization.LinkModListForm,
        ModListLinkProblem.NoVersion => Localization.LinkModListNoVersion,
        ModListLinkProblem.BadId => Localization.LinkModListBadId,
        ModListLinkProblem.BadVersion => Localization.LinkModListBadVersion,
        ModListLinkProblem.Duplicate => Localization.LinkModListDuplicate,
        ModListLinkProblem.TooMany => Localization.FormatLinkModListTooMany(BoreaLink.MaxModListEntries),
        ModListLinkProblem.BadName => Localization.FormatLinkModListBadName(BoreaLink.MaxNameLength),
        _ => null,
    };

    private async Task OpenLinkAsync(BoreaServices services, BoreaLink link)
    {
        if (_startLoad is { } load)
            await load.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);

        await EnsureDiscoverLoadedAsync();
        if (link.Kind == BoreaLinkKind.ModList)
        {
            await OpenModListLinkAsync(services, link);
            return;
        }

        var (listing, pack) = FindIndexContent(link.Id);
        if (listing is null && pack is null)
        {
            await RetryContentIndexAsync();
            (listing, pack) = FindIndexContent(link.Id);
        }

        IsSettingsOpen = false;
        if (listing is not null)
        {
            await OpenContentAsync(listing);
            if (link.Kind == BoreaLinkKind.Install && (listing.Type == ContentType.Mod || listing.Type.IsVehicleOrSave()))
                await PrepareLinkInstallAsync(services, listing, link.Version);
        }
        else if (pack is not null)
        {
            await OpenPackAsync(pack);
            if (link.Kind == BoreaLinkKind.Install)
                await PrepareLinkPackInstallAsync(services, pack, link.Version);
        }
        else
        {
            ShowErrorToast(() => Localization.FormatLinkNotInIndex(link.Id));
        }
    }

    /// <summary>
    /// Shows the mods of a modlist link in the modal of a modlist import, which creates a new instance on the click. A mod that
    /// is not a mod listing of the content index, or whose release is not offered for a new install or does not fit the installed
    /// game, is marked in its row, the same way a pack install refuses such a member, and then Borea plans nothing.
    /// </summary>
    private async Task OpenModListLinkAsync(BoreaServices services, BoreaLink link)
    {
        if (ModListImport is not null)
        {
            ShowErrorToast(() => Localization.LinkRefused);
            return;
        }

        if (link.Mods.Any(entry => FindIndexContent(entry.ModId).Listing is null))
            await RetryContentIndexAsync();

        var installed = services.InstalledVersion.GetInstalledVersion()?.Version;
        var rows = new List<ModListLinkRow>();
        var releases = new List<ModVersionMetadata>();
        foreach (var entry in link.Mods)
        {
            var listing = FindIndexContent(entry.ModId).Listing;
            var release = listing is { Type: ContentType.Mod } ? await services.ContentIndex.GetReleaseAsync(listing.ModId, entry.Version) : null;
            var problem = LinkEntryProblem(listing, release, installed);
            if (problem is null)
                releases.Add(release!);
            rows.Add(new ModListLinkRow(this, listing, entry, problem));
        }

        IsSettingsOpen = false;
        if (!await ShowNewModListAsync(services, new ModList(link.Name, link.Mods), link.Name ?? string.Empty, new ModListLink(rows, releases)))
            ShowErrorToast(() => Localization.LinkRefused);
    }

    /// <summary>
    /// Why the link cannot install <paramref name="entry"/>, or null when its release is offered and fits the game. The text
    /// does not name the mod or the version, because its row shows both.
    /// </summary>
    private Func<string>? LinkEntryProblem(DiscoverItem? listing, ModVersionMetadata? release, GameVersion? installed)
    {
        if (listing is null)
            return () => Localization.LinkModListRowNotInIndex;
        if (listing.Type != ContentType.Mod)
            return () => Localization.LinkModListRowNotAMod;

        return release switch
        {
            null => () => Localization.LinkModListRowVersionMissing,
            { Yanked: true } => () => Localization.FormatLinkModListRowYanked(release.YankedReason),
            { IsOffered: false } => () => Localization.LinkModListRowVersionGone,
            _ when Borea.Core.Game.Compatibility.Evaluate(release, installed) != GameCompatibility.Incompatible => null,
            _ when Borea.Core.Game.Compatibility.IsAboveUpperBound(release, installed) => () => Localization.FormatLinkModListRowAboveGameMax(release.GameMax!),
            _ => () => Localization.FormatLinkModListRowIncompatible(release.GameMin),
        };
    }

    /// <summary>Every listing and pack shares one id namespace, so a mod link that names a vehicle, a save or a pack still finds it.</summary>
    internal (DiscoverItem? Listing, PackItem? Pack) FindIndexContent(string id)
        => (_listings.FirstOrDefault(item => item.Source == "index" && ModIds.Equals(item.ModId, id)),
            _packs.FirstOrDefault(pack => pack.Metadata.Source == "index" && ModIds.Equals(pack.PackId, id)));

    private async Task PrepareLinkInstallAsync(BoreaServices services, DiscoverItem row, ModVersion? requested)
    {
        if (ActiveInstance is not { } instance)
        {
            ShowInstanceHintToast();
            return;
        }

        ModVersionMetadata? exact = null;
        Func<string>? note = null;
        if (requested is { } version)
            (exact, note) = await RequestedReleaseAsync(services, row, version);

        if (instance.InstalledVersionOf(row.ModId) is { } installed && (exact is null || exact.Version == installed))
        {
            ShowSuccessToast(() => WithNote(note, Localization.FormatLinkAlreadyInstalled(row.Name, instance.Name)));
            return;
        }

        // RFC 0110: a listing whose forums thread is gone offers no release, so the link says why instead of planning
        if (row.ForumsUnavailableSince is not null)
        {
            ShowErrorToast(() => row.ForumsGoneText ?? string.Empty);
            return;
        }

        Func<string> Request(ModVersionMetadata release) => () => WithNote(note, Localization.FormatLinkInstall($"{row.Name} {release.Version}", instance.Name));

        if (row.Type.IsVehicleOrSave())
        {
            // the name modal can stand between this plan and its wait, so the request travels with the plan
            if ((exact ?? row.LatestInChannel ?? await services.UserContent.GetLatestReleaseAsync(row.ModId, services.Settings.ReleaseChannel)) is { } item)
                await PlanContentAsync(row, instance.InstanceId, item, whenHeld: () => row.ShowLinkRequest(Request(item)));
            else
                row.InstallError = Localization.DiscoverNoRelease;
        }
        else
        {
            var chosen = exact;
            await PlanInstallAsync(
                row,
                async () => chosen ??= await services.ContentIndex.GetLatestReleaseInChannelAsync(row.ModId, services.Settings.ReleaseChannel),
                exact?.Version,
                confirm: true);

            if (row.IsConfirmingInstall && chosen is { } release)
                row.ShowLinkRequest(Request(release));
        }

        if (!row.IsConfirmingInstall && note is not null)
            Toasts.ShowMessage(ToastKind.Error, note);
    }

    private async Task<(ModVersionMetadata? Release, Func<string>? Note)> RequestedReleaseAsync(BoreaServices services, DiscoverItem row, ModVersion version)
    {
        var text = version.ToString();
        var release = row.Type.IsVehicleOrSave()
            ? await services.UserContent.GetReleaseAsync(row.ModId, version)
            : await services.ContentIndex.GetReleaseAsync(row.ModId, version);
        if (release is null)
            return (null, () => Localization.FormatLinkVersionMissing(text));

        if (release.Yanked)
            return (null, () => Localization.FormatLinkVersionYanked(text));

        // the download still works and no newer release is offered, so the version-gone text would be false
        if (release.ForumsUnavailableSince is { } since)
            return (null, () => Localization.FormatContentForumsGone(DateText(since)));

        if (!release.IsOffered)
            return (null, () => Localization.FormatLinkVersionGone(text));

        return (release, null);
    }

    private async Task PrepareLinkPackInstallAsync(BoreaServices services, PackItem pack, ModVersion? requested)
    {
        if (ActiveInstance is not { } instance)
        {
            ShowInstanceHintToast();
            return;
        }

        ModPackMetadata? exact = null;
        Func<string>? note = null;
        if (requested is { } version)
            (exact, note) = await RequestedPackVersionAsync(services, pack.PackId, version);

        var target = exact ?? pack.Metadata;
        if (target.Mods.All(pin => instance.Mods.Any(mod => ModIds.Equals(mod.ModId, pin.ContentId) && mod.Version == pin.Version)))
        {
            ShowSuccessToast(() => WithNote(note, Localization.FormatLinkAlreadyInstalled(pack.Name, instance.Name)));
            return;
        }

        await InstallPackAsync(pack, version: exact?.Version, confirm: true);

        if (pack.PendingInstall is not null)
            pack.ShowLinkRequest(() => WithNote(note, Localization.FormatLinkInstall($"{pack.Name} {target.Version}", instance.Name)));
        else if (note is not null)
            Toasts.ShowMessage(ToastKind.Error, note);
    }

    private async Task<(ModPackMetadata? Metadata, Func<string>? Note)> RequestedPackVersionAsync(BoreaServices services, string packId, ModVersion version)
    {
        var text = version.ToString();
        var available = (await services.ModPacks.GetAvailableVersionsAsync(packId))
            .Select(result => result.Metadata)
            .OfType<ModPackMetadata>()
            .FirstOrDefault(metadata => metadata.Version == version);
        if (available is not null)
            return (available, null);

        var retracted = (await services.ModPacks.GetVersionAsync(packId, version))?.Metadata is not null;
        return (null, retracted ? () => Localization.FormatLinkVersionYanked(text) : () => Localization.FormatLinkVersionMissing(text));
    }

    private static string WithNote(Func<string>? note, string text) => note is null ? text : $"{note()} {text}";
}
