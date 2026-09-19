using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;

namespace Borea.App.SingleInstance;

/// <summary>
/// The messages a second start and the running App exchange. Each message is a
/// 4-byte little-endian length and a UTF-8 JSON object with a version and a type.
/// The running App sends "hello" with its process id, the second start sends
/// "start" with its arguments, and the running App answers "accepted" or "rejected".
/// </summary>
internal static class HandoverProtocol
{
    public const int Version = 1;

    public const int MaxMessageBytes = 64 * 1024;

    public const int MaxArguments = 64;

    private const string Hello = "hello";
    private const string Start = "start";
    private const string Accepted = "accepted";
    private const string Rejected = "rejected";

    public static byte[] EncodeHello(int processId) =>
        Encode(Hello, writer => writer.WriteNumber("processId", processId));

    public static byte[] EncodeStart(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count > MaxArguments)
            throw new ArgumentException($"A start can hand over at most {MaxArguments} arguments.", nameof(arguments));

        var message = Encode(Start, writer =>
        {
            writer.WriteStartArray("arguments");
            foreach (var argument in arguments)
                writer.WriteStringValue(argument);
            writer.WriteEndArray();
        });

        if (message.Length > MaxMessageBytes)
            throw new ArgumentException($"The arguments are longer than {MaxMessageBytes} bytes.", nameof(arguments));

        return message;
    }

    public static byte[] EncodeAccepted() => Encode(Accepted, write: null);

    public static byte[] EncodeRejected() => Encode(Rejected, write: null);

    /// <summary>The process id of the running App, or null when the message is not a valid hello.</summary>
    public static int? DecodeHello(ReadOnlySpan<byte> message)
    {
        using var document = Parse(message, Hello);
        if (document is null
            || !document.RootElement.TryGetProperty("processId", out var processId)
            || processId.ValueKind != JsonValueKind.Number
            || !processId.TryGetInt32(out var id)
            || id <= 0)
            return null;

        return id;
    }

    /// <summary>The handed over arguments, or null when the message is not a valid start.</summary>
    public static IReadOnlyList<string>? DecodeStart(ReadOnlySpan<byte> message)
    {
        using var document = Parse(message, Start);
        if (document is null
            || !document.RootElement.TryGetProperty("arguments", out var array)
            || array.ValueKind != JsonValueKind.Array
            || array.GetArrayLength() > MaxArguments)
            return null;

        var arguments = new List<string>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return null;

            arguments.Add(item.GetString()!);
        }

        return arguments;
    }

    public static bool IsAccepted(ReadOnlySpan<byte> message)
    {
        using var document = Parse(message, Accepted);
        return document is not null;
    }

    public static async Task WriteAsync(Stream stream, byte[] message, CancellationToken cancellationToken)
    {
        if (message.Length > MaxMessageBytes)
            throw new ArgumentException($"A message can be at most {MaxMessageBytes} bytes.", nameof(message));

        var length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, message.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<byte[]> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxMessageBytes)
            throw new InvalidDataException($"A message of {length} bytes is outside the allowed size.");

        var message = new byte[length];
        await stream.ReadExactlyAsync(message, cancellationToken).ConfigureAwait(false);
        return message;
    }

    private static byte[] Encode(string type, Action<Utf8JsonWriter>? write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteString("type", type);
            write?.Invoke(writer);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static JsonDocument? Parse(ReadOnlySpan<byte> message, string expectedType)
    {
        // JsonDocument checks UTF-8 only when a string is read, which would throw later.
        if (message.IsEmpty || message.Length > MaxMessageBytes || !Utf8.IsValid(message))
            return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(message.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return null;
        }

        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("version", out var version)
            && version.ValueKind == JsonValueKind.Number
            && version.TryGetInt32(out var number)
            && number == Version
            && root.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && type.ValueEquals(expectedType))
            return document;

        document.Dispose();
        return null;
    }
}
