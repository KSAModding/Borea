using System;
using Avalonia.Controls.ApplicationLifetimes;
using Borea.Core.Logging;

namespace Borea.App.SingleInstance;

/// <summary>
/// Takes a borea:// link that macOS hands to the running App through the app bundle, and posts it
/// like a start that another process forwarded.
/// </summary>
internal static class ProtocolActivation
{
    /// <param name="lifetime">Null where the system never hands the App a link.</param>
    public static void Listen(IActivatableLifetime? lifetime, StartArgumentsInbox inbox, IBoreaLog? log)
    {
        if (lifetime is not null)
            lifetime.Activated += Handler(inbox, log);
    }

    /// <summary>Only an opened link is a start. The other activations, such as a click on the Dock icon, pass.</summary>
    internal static EventHandler<ActivatedEventArgs> Handler(StartArgumentsInbox inbox, IBoreaLog? log)
    {
        ArgumentNullException.ThrowIfNull(inbox);
        return (_, args) =>
        {
            if (args is not ProtocolActivatedEventArgs { Uri: { } uri })
                return;

            var link = uri.OriginalString;
            if (!inbox.Post(new StartArguments([link], Forwarded: true)))
                log?.Write($"Dropped the link {link}, because too many starts wait.");
        };
    }
}
