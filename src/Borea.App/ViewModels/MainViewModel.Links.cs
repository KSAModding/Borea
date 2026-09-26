using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.App.Links;
using Borea.Composition;
using Borea.Core.Links;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.App.ViewModels;

/// <summary>
/// borea:// links. A link opens a page of the content index, and an install link only prepares
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
        if (_linkRegistrationStarted || !OpenBoreaLinks)
            return;

        _linkRegistrationStarted = true;
        QueueLinkRegistration(enabled: true);
    }

    private void QueueLinkRegistration(bool enabled)
    {
        if (LinkHandler is not { } handler)
            return;

        var log = _services?.Log;
        _linkRegistration = _linkRegistration.ContinueWith(_ => handler.Apply(enabled, log), TaskScheduler.Default);
    }

    /// <summary>A link with an unescaped quote can arrive split into several arguments, so only a start with one argument opens it.</summary>
    internal async Task OpenStartLinkAsync(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 1)
            await OpenLinkAsync(arguments[0]);
        else
            RefuseLink(string.Join(' ', arguments), "the link came as more than one argument");
    }

    /// <summary>Opens what a borea:// link names. A refused link is logged and shown in a toast, and nothing else happens.</summary>
    internal async Task OpenLinkAsync(string text)
    {
        if (!BoreaLink.TryParse(text, out var link, out var refusal))
        {
            RefuseLink(text, refusal);
            return;
        }

        if (_services is not { } services)
            return;

        services.Log.Write($"Opening the link {link}.");
        try
        {
            await OpenLinkAsync(services, link);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            ShowErrorToast(() => Localization.LinkRefused, exception.Message);
        }
    }

    private void RefuseLink(string text, string reason)
    {
        _services?.Log.Write($"Refused the link {BoreaLink.ForLog(text)}, because {reason}.");
        Toasts.ShowMessage(ToastKind.Error, () => Localization.LinkRefused);
    }

    private async Task OpenLinkAsync(BoreaServices services, BoreaLink link)
    {
        if (_startLoad is { } load)
            await load.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);

        await EnsureDiscoverLoadedAsync();
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
            if (link.Kind == BoreaLinkKind.Install && listing.Type == ContentType.Mod)
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

    /// <summary>Mods and packs share one id namespace, so a mod link that names a pack still finds it.</summary>
    private (DiscoverItem? Listing, PackItem? Pack) FindIndexContent(string id)
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
        {
            var text = version.ToString();
            exact = await services.ContentIndex.GetReleaseAsync(row.ModId, version);
            if (exact is null)
                note = () => Localization.FormatLinkVersionMissing(text);
            else if (exact.Yanked)
            {
                exact = null;
                note = () => Localization.FormatLinkVersionYanked(text);
            }
        }

        if (instance.InstalledVersionOf(row.ModId) is { } installed && (exact is null || exact.Version == installed))
        {
            ShowSuccessToast(() => WithNote(note, Localization.FormatLinkAlreadyInstalled(row.Name, instance.Name)));
            return;
        }

        var chosen = exact;
        await PlanInstallAsync(
            row,
            async () => chosen ??= await services.ContentIndex.GetLatestReleaseInChannelAsync(row.ModId, services.Settings.ReleaseChannel),
            exact?.Version,
            confirm: true);

        if (row.IsConfirmingInstall && chosen is { } release)
            row.ShowLinkRequest(() => WithNote(note, Localization.FormatLinkInstall($"{row.Name} {release.Version}", instance.Name)));
        else if (note is not null)
            Toasts.ShowMessage(ToastKind.Error, note);
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
        {
            var text = version.ToString();
            exact = (await services.ModPacks.GetAvailableVersionsAsync(pack.PackId))
                .Select(result => result.Metadata)
                .OfType<ModPackMetadata>()
                .FirstOrDefault(metadata => metadata.Version == version);
            if (exact is null)
            {
                var retracted = (await services.ModPacks.GetVersionAsync(pack.PackId, version))?.Metadata is not null;
                note = retracted ? () => Localization.FormatLinkVersionYanked(text) : () => Localization.FormatLinkVersionMissing(text);
            }
        }

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

    private static string WithNote(Func<string>? note, string text) => note is null ? text : $"{note()} {text}";
}
