using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;

namespace Borea.App.Tests.Views;

internal static class HeadlessApp
{
    public static HeadlessUnitTestSession Start() => HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, FrameBufferFormat = PixelFormat.Rgba8888 });
}
