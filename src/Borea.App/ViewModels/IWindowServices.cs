using System.Threading.Tasks;

namespace Borea.App.ViewModels;

/// <summary>
/// The file pickers and the clipboard, which only the window can reach.
/// </summary>
public interface IWindowServices
{
    /// <summary>Returns the name of the saved file, or null when the user cancelled.</summary>
    Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string fileTypeName, string text);

    /// <summary>Null when the user cancelled.</summary>
    Task<PickedTextFile?> OpenTextFileAsync(string title, string fileTypeName);

    Task CopyTextAsync(string text);
}

public sealed record PickedTextFile(string Name, string Text);
