using Avalonia.Platform;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class MainWindowTests
{
    [Fact]
    public async Task Icon_IsTheBoreaIcon()
    {
        var session = HeadlessApp.Session;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var (hasIcon, boreaExists, avaloniaExists) = await session.Dispatch(() => (
            new MainWindow().Icon is not null,
            AssetLoader.Exists(new Uri("avares://Borea.App/Assets/borea.ico")),
            AssetLoader.Exists(new Uri("avares://Borea.App/Assets/avalonia-logo.ico"))), timeout.Token);

        Assert.True(hasIcon);
        Assert.True(boreaExists);
        Assert.False(avaloniaExists);
    }
}
