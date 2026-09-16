using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Borea.App.ViewModels;

namespace Borea.App.Views;

internal sealed class WindowServices(TopLevel topLevel) : IWindowServices
{
    private const string Extension = "toml";

    public async Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string fileTypeName, string text)
    {
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = Extension,
            FileTypeChoices = [FileType(fileTypeName)],
            ShowOverwritePrompt = true,
        });
        if (file is null)
            return null;

        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(text);
        return file.Name;
    }

    public async Task<PickedTextFile?> OpenTextFileAsync(string title, string fileTypeName)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [FileType(fileTypeName), FilePickerFileTypes.All],
        });
        if (files.Count == 0)
            return null;

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        return new PickedTextFile(files[0].Name, await reader.ReadToEndAsync());
    }

    public async Task CopyTextAsync(string text)
    {
        if (topLevel.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private static FilePickerFileType FileType(string name) => new(name) { Patterns = ["*." + Extension] };
}
