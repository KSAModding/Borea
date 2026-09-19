using Borea.App.SingleInstance;

namespace Borea.App.Tests.SingleInstance;

public sealed class StartArgumentsInboxTests
{
    [Fact]
    public void Open_HandsOverHeldStartsInOrder_ThenLaterOnes()
    {
        var inbox = new StartArgumentsInbox();
        var received = new List<string>();
        inbox.Post(new StartArguments([], Forwarded: false));
        inbox.Post(new StartArguments(["first"], Forwarded: true));

        inbox.Open(start => received.Add(start.Arguments.Count == 0 ? "own" : start.Arguments[0]));
        inbox.Post(new StartArguments(["second"], Forwarded: true));

        Assert.Equal(["own", "first", "second"], received);
    }

    [Fact]
    public void Post_BeforeOpen_HoldsAtMostTheCap_AndRejectsTheRest()
    {
        var inbox = new StartArgumentsInbox();
        for (var i = 0; i < StartArgumentsInbox.MaxPending; i++)
            Assert.True(inbox.Post(new StartArguments([i.ToString()], Forwarded: true)));
        Assert.False(inbox.Post(new StartArguments(["over"], Forwarded: true)));

        var received = new List<StartArguments>();
        inbox.Open(received.Add);

        Assert.Equal(StartArgumentsInbox.MaxPending, received.Count);
        Assert.Equal("0", received[0].Arguments[0]);
    }

    [Fact]
    public void Open_Twice_Throws()
    {
        var inbox = new StartArgumentsInbox();
        inbox.Open(_ => { });

        Assert.Throws<InvalidOperationException>(() => inbox.Open(_ => { }));
    }
}
