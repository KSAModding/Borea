using Borea.App.SingleInstance;

namespace Borea.App.Tests.SingleInstance;

public sealed class AppLockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaAppLock_" + Guid.NewGuid());

    private string LockPath => Path.Combine(_root, "nested", "app.lock");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SecondContender_DoesNotGetTheLock()
    {
        using var first = AppLock.TryAcquire(LockPath, out var firstError);
        var second = AppLock.TryAcquire(LockPath, out var secondError);

        Assert.NotNull(first);
        Assert.Null(firstError);
        Assert.Null(second);
        Assert.IsAssignableFrom<IOException>(secondError);
    }

    [Fact]
    public void ReleasedLock_CanBeTakenAgain()
    {
        AppLock.TryAcquire(LockPath, out _)!.Dispose();

        using var again = AppLock.TryAcquire(LockPath, out _);

        Assert.NotNull(again);
    }
}
