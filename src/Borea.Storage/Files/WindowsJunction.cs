using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Borea.Storage.Files;

/// <summary>
/// Creates a Windows junction, which is an empty directory that carries mount
/// point reparse data pointing at another directory.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsJunction
{
    private const int SetReparsePoint = 0x000900A4;
    private const uint MountPointTag = 0xA0000003;
    private const int MaximumReparseDataLength = 16 * 1024;

    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWriteDelete = 0x00000007;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;

    /// <summary>
    /// Creates <paramref name="linkPath"/> as a junction to
    /// <paramref name="targetPath"/>. Both paths must be full paths, and the
    /// link path must not exist yet.
    /// </summary>
    public static void Create(string linkPath, string targetPath)
    {
        var data = MountPointData(targetPath);
        Directory.CreateDirectory(linkPath);
        try
        {
            using var handle = CreateFileW(
                linkPath,
                GenericWrite,
                ShareReadWriteDelete,
                IntPtr.Zero,
                OpenExisting,
                BackupSemantics | OpenReparsePoint,
                IntPtr.Zero);

            if (handle.IsInvalid)
                throw new IOException($"Cannot open '{linkPath}' to make it a junction.", Marshal.GetLastPInvokeError());

            if (!DeviceIoControl(handle, SetReparsePoint, data, data.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw new IOException($"Cannot make '{linkPath}' a junction to '{targetPath}'.", Marshal.GetLastPInvokeError());
        }
        catch
        {
            TryRemoveEmptyDirectory(linkPath);
            throw;
        }
    }

    /// <summary>
    /// Builds a REPARSE_DATA_BUFFER for a mount point. It holds the target twice,
    /// once as the NT path the filesystem resolves and once as the path a user
    /// reads, and both are stored with a terminator behind them.
    /// </summary>
    private static byte[] MountPointData(string targetPath)
    {
        var substituteName = Encoding.Unicode.GetBytes(@"\??\" + targetPath);
        var printName = Encoding.Unicode.GetBytes(targetPath);
        var pathBufferLength = substituteName.Length + 2 + printName.Length + 2;
        var reparseDataLength = 8 + pathBufferLength;
        if (8 + reparseDataLength > MaximumReparseDataLength)
            throw new IOException($"The junction target '{targetPath}' is too long for a reparse point.");

        var buffer = new byte[8 + reparseDataLength];
        var fields = buffer.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(fields, MountPointTag);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[4..], (ushort)reparseDataLength);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[6..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[8..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[10..], (ushort)substituteName.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[12..], (ushort)(substituteName.Length + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(fields[14..], (ushort)printName.Length);
        substituteName.CopyTo(fields[16..]);
        printName.CopyTo(fields[(16 + substituteName.Length + 2)..]);
        return buffer;
    }

    /// <summary>
    /// Clears the directory the failed junction left behind, without replacing
    /// the error that caused it.
    /// </summary>
    private static void TryRemoveEmptyDirectory(string path)
    {
        try
        {
            Directory.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string path,
        uint access,
        uint share,
        IntPtr security,
        uint disposition,
        uint flags,
        IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle handle,
        int controlCode,
        byte[] input,
        int inputLength,
        IntPtr output,
        int outputLength,
        out int returned,
        IntPtr overlapped);
}
