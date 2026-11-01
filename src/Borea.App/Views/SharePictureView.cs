using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace Borea.App.Views;

/// <summary>Shows a small copy of a picture that has no record of an image yet, decoded on the thread pool, because a picture of a share can be 10 MB.</summary>
public sealed class SharePictureView : Image
{
    private const int DecodeWidth = 240;

    public static readonly StyledProperty<byte[]?> BytesProperty =
        AvaloniaProperty.Register<SharePictureView, byte[]?>(nameof(Bytes));

    private int _generation;

    public byte[]? Bytes
    {
        get => GetValue(BytesProperty);
        set => SetValue(BytesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BytesProperty && this.IsAttachedToVisualTree())
            Show();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Show();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Release();
    }

    private void Show()
    {
        Release();
        if (Bytes is { Length: > 0 } bytes)
            _ = DecodeAsync(bytes, _generation);
    }

    private void Release()
    {
        _generation++;
        var shown = Source as Bitmap;
        Source = null;
        shown?.Dispose();
    }

    private async Task DecodeAsync(byte[] bytes, int generation)
    {
        Bitmap? bitmap = null;
        try
        {
            bitmap = await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes, writable: false);
                return Bitmap.DecodeToWidth(stream, DecodeWidth);
            });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Bytes that pass the check of the format and still do not decode leave the slot empty, and the host decides about them.
        }

        if (generation == _generation)
            Source = bitmap;
        else
            bitmap?.Dispose();
    }
}
