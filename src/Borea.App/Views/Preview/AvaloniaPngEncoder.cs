using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Borea.Preview.Rendering;

namespace Borea.App.Views.Preview;

/// <summary>Encodes a preview frame as a PNG with the bitmap encoder of the Avalonia platform. It runs on any thread.</summary>
internal sealed class AvaloniaPngEncoder : IPngEncoder
{
    public byte[] Encode(FrameBuffer frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var handle = GCHandle.Alloc(frame.Rgba, GCHandleType.Pinned);
        try
        {
            // The bitmap copies the pixels, so the pin ends with this call.
            using var bitmap = new Bitmap(
                PixelFormat.Rgba8888, AlphaFormat.Unpremul, handle.AddrOfPinnedObject(), new PixelSize(frame.Width, frame.Height), new Vector(96, 96), frame.Width * 4);
            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            return stream.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }
}
