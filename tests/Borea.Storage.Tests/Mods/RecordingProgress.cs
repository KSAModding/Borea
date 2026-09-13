namespace Borea.Storage.Tests.Mods;

/// <summary>Keeps every report in order, on the reporting thread.</summary>
internal sealed class RecordingProgress<T> : IProgress<T>
{
    public List<T> Reports { get; } = new();

    public void Report(T value) => Reports.Add(value);
}
