using Borea.Core.Logging;

namespace Borea.Storage.Tests.Logging;

internal sealed class RecordingLog : IBoreaLog
{
    public List<string> Messages { get; } = [];

    public List<Exception?> Exceptions { get; } = [];

    public string CurrentFilePath => "borea.log";

    public void Write(string message)
    {
        Messages.Add(message);
        Exceptions.Add(null);
    }

    public void Write(string message, Exception exception)
    {
        Messages.Add(message);
        Exceptions.Add(exception);
    }

    public IReadOnlyList<string> ReadRecentLines(int maxLines) => [];
}
