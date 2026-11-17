using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Borea.App.ViewModels;
using Borea.Core.Files;

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

        await WriteExportAsync(file, async stream =>
        {
            await using var writer = new StreamWriter(stream, leaveOpen: true);
            await writer.WriteAsync(text);
        });
        return file.Name;
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedFileName, string extension, string fileTypeName, Func<Stream, Task> write)
    {
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(fileTypeName) { Patterns = ["*." + extension] }],
            ShowOverwritePrompt = true,
        });
        if (file is null)
            return null;

        await WriteExportAsync(file, write);
        return file.Name;
    }

    private static async Task WriteExportAsync(IStorageFile file, Func<Stream, Task> write)
    {
        if (file.TryGetLocalPath() is { } path)
        {
            await WriteLocalExportAsync(path, write);
            return;
        }

        if ((await file.GetBasicPropertiesAsync()).Size != 0)
            throw new IOException("This storage provider cannot safely replace an existing file. Choose a new file.");

        await using var stream = await file.OpenWriteAsync();
        await write(stream);
    }

    internal static async Task WriteLocalExportAsync(string path, Func<Stream, Task> write)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await write(stream);
            }
            await FileReplace.MoveOverAsync(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }
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

    public async Task<PickedBinaryFile?> OpenImageFileAsync(string title, string fileTypeName, long maxBytes)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(fileTypeName) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] }, FilePickerFileTypes.All],
        });
        if (files.Count == 0)
            return null;

        await using var stream = await files[0].OpenReadAsync();
        using var bytes = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while (bytes.Length <= maxBytes && (read = await stream.ReadAsync(buffer)) > 0)
            bytes.Write(buffer, 0, read);
        return new PickedBinaryFile(files[0].Name, bytes.ToArray());
    }

    public async Task CopyTextAsync(string text)
    {
        if (topLevel.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private static FilePickerFileType FileType(string name) => new(name) { Patterns = ["*." + Extension] };
}
