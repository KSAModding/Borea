using System;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;

namespace Borea.App.Tests.Views;

/// <summary>
/// The one headless session of the test process. Avalonia keeps one application
/// per process, so a second session tears down the state that the first one
/// still uses. The session lives as long as the process and no test disposes it.
/// </summary>
internal static class HeadlessApp
{
    private static readonly Lazy<HeadlessUnitTestSession> Instance =
        new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessApp)), LazyThreadSafetyMode.ExecutionAndPublication);

    public static HeadlessUnitTestSession Session => Instance.Value;

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, FrameBufferFormat = PixelFormat.Rgba8888 });
}

/// <summary>
/// Holds every test that renders through <see cref="HeadlessApp"/> in one xunit
/// collection, so two of them never share the session at the same time. A forced
/// render timer tick and a captured frame are per process, so a parallel test
/// would advance the animation of another one.
/// </summary>
[CollectionDefinition(HeadlessCollection.Name)]
public sealed class HeadlessCollection
{
    public const string Name = "Headless rendering";
}
