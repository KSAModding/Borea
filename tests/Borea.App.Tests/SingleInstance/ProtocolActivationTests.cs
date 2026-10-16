using Avalonia.Controls.ApplicationLifetimes;
using Borea.App.SingleInstance;
using Borea.App.Tests.Links;

namespace Borea.App.Tests.SingleInstance;

/// <summary>Avalonia's lifetimes cannot be faked, so the tests call the handler as the lifetime would.</summary>
public sealed class ProtocolActivationTests
{
    private readonly StartArgumentsInbox _inbox = new();
    private readonly RecordingLog _log = new();
    private readonly List<StartArguments> _received = [];

    [Fact]
    public void Handler_OnAnOpenedLink_PostsItAsAForwardedStart()
    {
        _inbox.Open(_received.Add);
        var handler = ProtocolActivation.Handler(_inbox, _log);

        handler(null, new ProtocolActivatedEventArgs(new Uri("borea://mod/123")));

        var start = Assert.Single(_received);
        Assert.Equal(["borea://mod/123"], start.Arguments);
        Assert.True(start.Forwarded);
        Assert.Empty(_log.Lines);
    }

    [Fact]
    public void Handler_OnAnotherActivation_PostsNothing()
    {
        _inbox.Open(_received.Add);
        var handler = ProtocolActivation.Handler(_inbox, _log);

        handler(null, new ActivatedEventArgs(ActivationKind.Reopen));

        Assert.Empty(_received);
    }

    [Fact]
    public void Handler_BeforeTheWindowOpens_HoldsTheLink()
    {
        var handler = ProtocolActivation.Handler(_inbox, _log);
        handler(null, new ProtocolActivatedEventArgs(new Uri("borea://mod/123")));

        _inbox.Open(_received.Add);

        Assert.Equal(["borea://mod/123"], Assert.Single(_received).Arguments);
    }

    [Fact]
    public void Handler_WhenTooManyStartsWait_LogsTheDroppedLink()
    {
        for (var i = 0; i < StartArgumentsInbox.MaxPending; i++)
            _inbox.Post(new StartArguments([], Forwarded: false));
        var handler = ProtocolActivation.Handler(_inbox, _log);

        handler(null, new ProtocolActivatedEventArgs(new Uri("borea://mod/123")));
        _inbox.Open(_received.Add);

        Assert.Equal(StartArgumentsInbox.MaxPending, _received.Count);
        Assert.Contains("borea://mod/123", Assert.Single(_log.Lines));
    }

    [Fact]
    public void Listen_WithoutALifetime_DoesNothing()
    {
        _inbox.Open(_received.Add);

        ProtocolActivation.Listen(null, _inbox, _log);

        Assert.Empty(_received);
        Assert.Empty(_log.Lines);
    }
}
