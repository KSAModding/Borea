using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;
using Borea.App.Tests.ViewModels;

namespace Borea.App.Tests.Views;

/// <summary>
/// The one headless session of the test process. The session lives as long as the
/// process and no test disposes it, but the application does not, because the
/// session isolates every test. Each dispatch builds the application, runs its
/// delegate, and takes the application and the Avalonia dispatcher down again.
/// That dispatcher also runs queued work only while its dispatch is in flight, so
/// anything a test starts on it has to finish inside the same dispatch. Use
/// <see cref="RunAsync"/>, which waits for it.
/// </summary>
internal static class HeadlessApp
{
    private static readonly Lazy<HeadlessUnitTestSession> Instance =
        new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessApp)), LazyThreadSafetyMode.ExecutionAndPublication);

    public static HeadlessUnitTestSession Session => Instance.Value;

    /// <summary>How long one dispatch may take before it gives up.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs <paramref name="body"/> on the headless thread, where the Avalonia
    /// dispatcher of this dispatch runs the queued work of the body until it is
    /// done.
    /// </summary>
    /// <remarks>
    /// The dispatch gets no cancellation token on purpose. A token that fires while
    /// the body still runs leaves the session thread in a blocking wait inside
    /// Avalonia, and that one thread carries every later test of the process. The
    /// limit here bounds the body instead, so a body that does not finish fails its
    /// own test and leaves the session usable.
    /// </remarks>
    public static Task<T> RunAsync<T>(Func<Task<T>> body) =>
        Session.Dispatch(() => body().WaitAsync(Limit), CancellationToken.None);

    /// <summary>
    /// Runs <paramref name="body"/> as <see cref="RunAsync{T}(Func{Task{T}})"/> does
    /// and lets the background work of <paramref name="harness"/> finish before the
    /// dispatch returns. A click starts work that continues on the Avalonia
    /// dispatcher of this dispatch, and that dispatcher is gone once the dispatch
    /// returned, so work that is left behind never continues and whoever waits for
    /// it waits for ever.
    /// </summary>
    public static Task<T> RunAsync<T>(ViewModelHarness harness, Func<Task<T>> body) =>
        RunAsync(async () =>
        {
            var result = await body();
            await harness.WhenIdleAsync();
            return result;
        });

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
