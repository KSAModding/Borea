using System.Globalization;
using Borea.Core.Settings;

namespace Borea.Cli.Output;

/// <summary>
/// Writes one line when a library move to another volume starts a stage, and
/// one for every tenth of the bytes copied. The lines go to the error stream,
/// like the install progress.
/// </summary>
internal sealed class LibraryMoveProgressOutput : IProgress<LibraryMoveProgress>
{
    private readonly TextWriter _writer;
    private LibraryMoveStage? _stage;
    private int _tenths;

    public LibraryMoveProgressOutput(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void Report(LibraryMoveProgress value)
    {
        if (_stage != value.Stage)
        {
            _stage = value.Stage;
            _writer.WriteLine(value.Stage == LibraryMoveStage.Copying
                ? string.Create(CultureInfo.InvariantCulture, $"Copying {value.TotalFiles} {(value.TotalFiles == 1 ? "file" : "files")} ({value.TotalBytes / 1_000_000.0:0.0} MB)")
                : "Deleting the old files");
        }

        if (value.Stage != LibraryMoveStage.Copying)
            return;

        var tenths = (int)(value.PercentComplete / 10);
        if (tenths <= _tenths || (tenths == 10 && value.Files < value.TotalFiles))
            return;

        _tenths = tenths;
        _writer.WriteLine($"Copied {tenths * 10}% ({value.Files} of {value.TotalFiles} files)");
    }
}
