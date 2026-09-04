using System.ComponentModel;
using Borea.Core.Launch;
using Borea.Storage.Launch;

namespace Borea.Storage.Tests.Launch;

public sealed class ProcessStarterTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest " + Guid.NewGuid());
    private readonly ProcessStarter _starter = new();

    public ProcessStarterTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>
    /// A script that writes the plan's variable and an inherited one into
    /// files next to it, so the files prove the directory and both variables.
    /// </summary>
    private LaunchPlan ProbePlan()
    {
        var variables = new Dictionary<string, string> { ["BOREA_PROBE"] = "probe-value" };

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(_tempRoot, "probe.cmd");
            File.WriteAllText(script, "@echo off\r\necho %BOREA_PROBE%> env.txt\r\necho %PATH%> inherited.txt\r\n");
            return new LaunchPlan(Path.Combine(Environment.SystemDirectory, "cmd.exe"), new[] { "/c", script }, _tempRoot, variables);
        }

        var shellScript = Path.Combine(_tempRoot, "probe.sh");
        File.WriteAllText(shellScript, "printf '%s' \"$BOREA_PROBE\" > env.txt\nprintf '%s' \"$PATH\" > inherited.txt\n");
        return new LaunchPlan("/bin/sh", new[] { shellScript }, _tempRoot, variables);
    }

    private static void WaitForExit(IStartedProcess process)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!process.HasExited && DateTime.UtcNow < deadline)
            Thread.Sleep(50);
    }

    [Fact]
    public void Start_RunsTheExecutableInTheWorkingDirectoryWithTheVariables()
    {
        using var process = _starter.Start(ProbePlan());

        Assert.True(process.Id > 0);
        WaitForExit(process);
        Assert.True(process.HasExited);

        var written = Path.Combine(_tempRoot, "env.txt");
        Assert.True(File.Exists(written), "The script did not run in the plan's working directory.");
        Assert.Equal("probe-value", File.ReadAllText(written).Trim());
        Assert.NotEqual(string.Empty, File.ReadAllText(Path.Combine(_tempRoot, "inherited.txt")).Trim());
    }

    [Fact]
    public void Start_MissingExecutable_ThrowsWin32Exception()
    {
        var plan = new LaunchPlan(
            Path.Combine(_tempRoot, "missing.exe"),
            Array.Empty<string>(),
            _tempRoot,
            new Dictionary<string, string>());

        Assert.Throws<Win32Exception>(() => _starter.Start(plan));
    }

    [Fact]
    public void Start_NullPlan_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _starter.Start(null!));
    }

    public void Dispose()
    {
        // A child that outlived the wait still holds the directory
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
