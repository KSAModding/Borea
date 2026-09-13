using System.Text.Json;
using Borea.Core.Launch;
using Borea.Storage.Launch;

// "host <directory>" starts a child through ProcessStarter and prints its id.
// "child <arguments>" records what it received and writes to its output until a stop file appears.
return args switch
{
    ["host", var directory] => Host(directory),
    ["child", ..] => Child(args[1..]),
    _ => 2,
};

static int Host(string directory)
{
    var plan = new LaunchPlan(
        Environment.ProcessPath!,
        new[] { Path.Combine(AppContext.BaseDirectory, "LaunchProbeFixture.dll"), "child" },
        directory,
        new Dictionary<string, string>());

    using var process = new ProcessStarter().Start(plan);
    Console.WriteLine($"Process id: {process.Id}");
    return 0;
}

static int Child(string[] arguments)
{
    var record = new ChildRecord(arguments, Environment.CurrentDirectory, Environment.GetEnvironmentVariable("BOREA_PROBE"), Environment.ProcessId);
    WriteAtomically("record.json", JsonSerializer.Serialize(record));

    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
    var lines = 0;
    while (!File.Exists("stop") && DateTime.UtcNow < deadline)
    {
        Console.Out.WriteLine($"line {lines}");
        Console.Out.Flush();
        Console.Error.WriteLine($"line {lines}");
        Console.Error.Flush();
        lines++;

        if (lines % 10 == 0)
            WriteAtomically("progress.txt", lines.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Thread.Sleep(20);
    }

    return 0;
}

// A reader never sees a half written file.
static void WriteAtomically(string path, string contents)
{
    var temporary = path + ".tmp";
    File.WriteAllText(temporary, contents);
    File.Move(temporary, path, overwrite: true);
}

internal sealed record ChildRecord(string[] Arguments, string WorkingDirectory, string? Variable, int ProcessId);
