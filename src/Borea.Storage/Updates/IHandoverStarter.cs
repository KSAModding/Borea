namespace Borea.Storage.Updates;

/// <summary>Starts a new Borea build so that it can take over from the running one.</summary>
public interface IHandoverStarter
{
    /// <summary>Starts <paramref name="programPath"/> in its own folder. Throws the operating system's error when it does not start.</summary>
    void Start(string programPath, IReadOnlyList<string> arguments);
}
