using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Borea.Core.Launch;
using Borea.Storage.Launch;
using static Borea.Storage.Tests.Launch.LaunchProbe;

namespace Borea.Storage.Tests.Launch;

public sealed class ProcessStarterTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest " + Guid.NewGuid());
    private readonly ProcessStarter _starter = new();
    private readonly List<int> _probeIds = new();

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

    /// <summary>A shell script that writes to both streams after a go file appears, then writes survived.txt.</summary>
    private LaunchPlan ShellWriterPlan()
    {
        var shellScript = Path.Combine(_tempRoot, "writer.sh");
        File.WriteAllText(
            shellScript,
            "while [ ! -f go ]; do sleep 1; done\n"
            + "i=0\n"
            + "while [ \"$i\" -lt 100 ]; do echo \"line $i\"; echo \"line $i\" >&2; i=$((i+1)); done\n"
            + "echo survived > survived.txt\n");
        return new LaunchPlan("/bin/sh", new[] { shellScript }, _tempRoot, new Dictionary<string, string>());
    }

    private LaunchPlan ChildPlan(IEnumerable<string> arguments, IReadOnlyDictionary<string, string> variables) =>
        new(DotnetHost(), new[] { ProbeAssembly(), "child" }.Concat(arguments).ToArray(), _tempRoot, variables);

    /// <summary>A loader that restarts itself, with the instance root in both the flag and the variable.</summary>
    private LaunchPlan RestartPlan() => new(
        DotnetHost(),
        new[] { ProbeAssembly(), "-InstancePath", _tempRoot, "restart" },
        _tempRoot,
        new Dictionary<string, string> { [InstanceVariable] = _tempRoot });

    private bool ChildWrote() => File.Exists(Path.Combine(_tempRoot, "wrote"));

    [Fact]
    public async Task Start_RunsTheExecutableInTheWorkingDirectoryWithTheVariables()
    {
        using var process = _starter.Start(ProbePlan());

        Assert.True(process.Id > 0);
        Assert.True(await process.WaitForExitAsync(Patience));

        var written = Path.Combine(_tempRoot, "env.txt");
        Assert.True(File.Exists(written), "The script did not run in the plan's working directory.");
        Assert.Equal("probe-value", File.ReadAllText(written).Trim());
        Assert.NotEqual(string.Empty, File.ReadAllText(Path.Combine(_tempRoot, "inherited.txt")).Trim());
    }

    [Fact]
    public void Start_PassesTheArgumentsTheWorkingDirectoryAndTheVariables()
    {
        var arguments = new[] { "-InstancePath", Path.Combine(_tempRoot, "an instance"), "quote\"inside", string.Empty };
        var variables = new Dictionary<string, string> { ["BOREA_PROBE"] = "probe value" };

        using var process = _starter.Start(ChildPlan(arguments, variables));
        _probeIds.Add(process.Id);

        var recordPath = Path.Combine(_tempRoot, "record.json");
        Assert.True(WaitFor(() => File.Exists(recordPath) || process.HasExited), "The child neither wrote a record nor ended.");
        Assert.True(File.Exists(recordPath), $"The child ended with exit code {process.ExitCode} and wrote no record.");

        using var record = JsonDocument.Parse(File.ReadAllText(recordPath));
        var root = record.RootElement;
        Assert.Equal(arguments, root.GetProperty("Arguments").EnumerateArray().Select(argument => argument.GetString()).ToArray());
        // The child wrote its record relative to its working directory, so the reported directory holds it.
        // A path comparison would fail on macOS, where the temp folder is reached through a symlink.
        Assert.True(File.Exists(Path.Combine(root.GetProperty("WorkingDirectory").GetString()!, "record.json")));
        Assert.Equal("probe value", root.GetProperty("Variable").GetString());
    }

    [Fact]
    public void Start_ChildThatWritesToItsOutput_KeepsRunningWithoutAReader()
    {
        using var process = _starter.Start(ChildPlan(Array.Empty<string>(), new Dictionary<string, string>()));
        _probeIds.Add(process.Id);

        Assert.True(WaitFor(() => ChildWrote() || process.HasExited), "The child is stuck on its output.");
        Assert.False(process.HasExited, $"The child ended with exit code {process.ExitCode} after it wrote to its output.");
    }

    [UnixFact("Windows has no SIGPIPE.")]
    public void Start_ShellChildThatWritesToItsOutput_IsNotEndedBySigpipe()
    {
        using var process = _starter.Start(ShellWriterPlan());
        _probeIds.Add(process.Id);

        File.WriteAllText(Path.Combine(_tempRoot, "go"), string.Empty);

        var survived = Path.Combine(_tempRoot, "survived.txt");
        Assert.True(WaitFor(() => File.Exists(survived)), "The writes to the child's output ended the child.");
    }

    [Fact]
    public async Task Start_LoaderThatRestartsItself_EndsOnlyWithItsRestart()
    {
        using var process = _starter.Start(RestartPlan());
        _probeIds.Add(process.Id);

        var restart = RestartRecord(_tempRoot);
        Assert.NotNull(restart);
        _probeIds.Add(restart.Value.ProcessId);
        Assert.True(WaitFor(() => process.HasExited), "The loader did not exit after it restarted itself.");
        Assert.Equal(0, process.ExitCode);

        // like the game's Restart button, the probe restarts without the instance flag, so only the variable still names the instance
        Assert.Empty(restart.Value.Arguments);
        Assert.Equal(_tempRoot, restart.Value.Variable);

        Assert.True(WaitFor(() => process.RecentOutput.Contains($"Restarted as {restart.Value.ProcessId}")), "Borea did not read what the restart wrote.");
        Assert.False(process.HasEnded);
        Assert.False(await process.WaitForExitAsync(TimeSpan.FromMilliseconds(200)));

        Signal(_tempRoot, "go");
        Signal(_tempRoot, "stop");

        Assert.True(await process.WaitForExitAsync(Patience), "The launch did not end with its restart.");
        Assert.True(process.HasEnded);
    }

    [Fact]
    public void Dispose_AfterTheLoaderRestartedItself_LeavesTheRestartRunning()
    {
        using var process = _starter.Start(RestartPlan());
        _probeIds.Add(process.Id);
        var restart = RestartRecord(_tempRoot);
        Assert.NotNull(restart);
        _probeIds.Add(restart.Value.ProcessId);
        Assert.True(WaitFor(() => process.HasExited), "The loader did not exit after it restarted itself.");

        process.Dispose();
        Signal(_tempRoot, "go");

        Assert.True(WaitFor(() => File.Exists(Path.Combine(_tempRoot, "restarted-wrote")) || !IsAlive(restart.Value.ProcessId)), "The restart is stuck on its output.");
        Assert.True(IsAlive(restart.Value.ProcessId), "The restart stopped when Borea released the loader.");
    }

    [Fact]
    public async Task Start_FromACallerWhoseOutputIsPiped_ReturnsWhileTheChildRuns()
    {
        // The host stands in for the CLI, and this test reads its output through pipes.
        var hostInfo = new ProcessStartInfo
        {
            FileName = DotnetHost(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        hostInfo.ArgumentList.Add(ProbeAssembly());
        hostInfo.ArgumentList.Add("host");
        hostInfo.ArgumentList.Add(_tempRoot);

        using var host = Process.Start(hostInfo)!;
        try
        {
            var output = host.StandardOutput.ReadToEndAsync();
            var error = host.StandardError.ReadToEndAsync();

            var childId = RecordedProcessId();
            Assert.NotNull(childId);
            _probeIds.Add(childId.Value);

            var closed = Task.WhenAll(output, error);
            Assert.True(await Task.WhenAny(closed, Task.Delay(Patience)) == closed, "The caller's output stayed open after the host exited.");
            Assert.True(host.WaitForExit(Patience), "The host did not exit.");
            Assert.Equal(0, host.ExitCode);
            Assert.Equal(childId, ChildIdFrom(await output));

            Assert.True(WaitFor(() => ChildWrote() || !IsAlive(childId.Value)), "The child is stuck on its output.");
            Assert.True(IsAlive(childId.Value), "The child was not running when the caller's output closed.");
        }
        finally
        {
            if (!host.HasExited)
                host.Kill();
        }
    }

    private int? RecordedProcessId()
    {
        var path = Path.Combine(_tempRoot, "record.json");
        if (!WaitFor(() => File.Exists(path)))
            return null;

        using var record = JsonDocument.Parse(File.ReadAllText(path));
        return record.RootElement.GetProperty("ProcessId").GetInt32();
    }

    private static int? ChildIdFrom(string output)
    {
        const string Prefix = "Process id: ";
        var line = output.Split('\n').Select(text => text.Trim()).FirstOrDefault(text => text.StartsWith(Prefix, StringComparison.Ordinal));
        return line is not null && int.TryParse(line.AsSpan(Prefix.Length), out var id) ? id : null;
    }

    [Fact]
    public async Task Start_KeepsWhatTheProcessWritesAndItsExitCode()
    {
        LaunchPlan plan;
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(_tempRoot, "crash.cmd");
            File.WriteAllText(script, "@echo off\r\necho starting\r\necho broken assembly 1>&2\r\nexit /b 3\r\n");
            plan = new LaunchPlan(Path.Combine(Environment.SystemDirectory, "cmd.exe"), new[] { "/c", script }, _tempRoot, new Dictionary<string, string>());
        }
        else
        {
            var script = Path.Combine(_tempRoot, "crash.sh");
            File.WriteAllText(script, "echo starting\necho broken assembly >&2\nexit 3\n");
            plan = new LaunchPlan("/bin/sh", new[] { script }, _tempRoot, new Dictionary<string, string>());
        }

        using var process = _starter.Start(plan);

        Assert.True(await process.WaitForExitAsync(Patience));
        Assert.Equal(3, process.ExitCode);
        Assert.Contains("starting", process.RecentOutput);
        Assert.Contains("broken assembly", process.RecentOutput.Select(line => line.Trim()));
    }

    [Fact]
    public async Task Dispose_AfterTheProcessEnded_KeepsItsIdAndExitCode()
    {
        var plan = OperatingSystem.IsWindows()
            ? new LaunchPlan(Path.Combine(Environment.SystemDirectory, "cmd.exe"), new[] { "/c", "exit 3" }, _tempRoot, new Dictionary<string, string>())
            : new LaunchPlan("/bin/sh", new[] { "-c", "exit 3" }, _tempRoot, new Dictionary<string, string>());
        var process = _starter.Start(plan);
        var id = process.Id;
        Assert.True(await process.WaitForExitAsync(Patience));

        process.Dispose();

        Assert.Equal(id, process.Id);
        Assert.True(process.HasExited);
        Assert.Equal(3, process.ExitCode);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => process.WaitForExitAsync(Patience));
    }

    [Fact]
    public async Task Dispose_WhileAWaitWithoutEndRuns_EndsTheWait()
    {
        var process = _starter.Start(ChildPlan(Array.Empty<string>(), new Dictionary<string, string>()));
        _probeIds.Add(process.Id);
        var wait = process.WaitForExitAsync(Timeout.InfiniteTimeSpan);

        process.Dispose();

        Assert.False(await wait.WaitAsync(Patience));
        Assert.False(process.HasExited);
        Assert.Null(process.ExitCode);
    }

    [Fact]
    public async Task WaitForExit_RunningProcess_ReturnsFalseAfterTheTimeout()
    {
        using var process = _starter.Start(ChildPlan(Array.Empty<string>(), new Dictionary<string, string>()));
        _probeIds.Add(process.Id);

        Assert.False(await process.WaitForExitAsync(TimeSpan.FromMilliseconds(200)));
        Assert.Null(process.ExitCode);
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
        try
        {
            Signal(_tempRoot, "go");
            Signal(_tempRoot, "stop");
        }
        catch (IOException)
        {
        }

        foreach (var id in _probeIds)
            StopProbe(id);

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
