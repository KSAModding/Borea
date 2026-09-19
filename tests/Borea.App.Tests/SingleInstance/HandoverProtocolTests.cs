using System.Buffers.Binary;
using System.Text;
using Borea.App.SingleInstance;

namespace Borea.App.Tests.SingleInstance;

public sealed class HandoverProtocolTests
{
    [Fact]
    public void Start_RoundTrips()
    {
        var umlauts = new string([(char)0xFC, (char)0xDF]);
        string[] arguments = ["borea://install/Example?version=1.2.0", "", umlauts + " \"quoted\" \\ path"];

        var decoded = HandoverProtocol.DecodeStart(HandoverProtocol.EncodeStart(arguments));

        Assert.Equal(arguments, decoded);
    }

    [Fact]
    public void Start_WithoutArguments_RoundTrips()
    {
        Assert.Empty(HandoverProtocol.DecodeStart(HandoverProtocol.EncodeStart([]))!);
    }

    [Fact]
    public void Hello_RoundTrips()
    {
        Assert.Equal(4242, HandoverProtocol.DecodeHello(HandoverProtocol.EncodeHello(4242)));
    }

    [Fact]
    public void Accepted_RoundTrips()
    {
        Assert.True(HandoverProtocol.IsAccepted(HandoverProtocol.EncodeAccepted()));
    }

    [Fact]
    public void Decode_OtherType_IsRejected()
    {
        Assert.Null(HandoverProtocol.DecodeStart(HandoverProtocol.EncodeHello(1)));
        Assert.Null(HandoverProtocol.DecodeHello(HandoverProtocol.EncodeStart([])));
        Assert.False(HandoverProtocol.IsAccepted(HandoverProtocol.EncodeStart([])));
    }

    [Theory]
    [InlineData("""{"version":2,"type":"start","arguments":[]}""")]
    [InlineData("""{"version":"1","type":"start","arguments":[]}""")]
    [InlineData("""{"type":"start","arguments":[]}""")]
    [InlineData("""{"version":1,"type":"start"}""")]
    [InlineData("""{"version":1,"type":"start","arguments":"a"}""")]
    [InlineData("""{"version":1,"type":"start","arguments":["a",1]}""")]
    [InlineData("""{"version":1,"type":"start","arguments":[null]}""")]
    [InlineData("""{"version":1,"type":"start","arguments":[["a"]]}""")]
    [InlineData("""[{"version":1,"type":"start","arguments":[]}]""")]
    [InlineData("""{"version":1,"type":"start","arguments":[]""")]
    [InlineData("not json")]
    [InlineData("")]
    public void DecodeStart_BadInput_IsRejected(string json)
    {
        Assert.Null(HandoverProtocol.DecodeStart(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("""{"version":1,"type":"hello","processId":0}""")]
    [InlineData("""{"version":1,"type":"hello","processId":-5}""")]
    [InlineData("""{"version":1,"type":"hello","processId":1.5}""")]
    [InlineData("""{"version":1,"type":"hello","processId":99999999999}""")]
    [InlineData("""{"version":1,"type":"hello"}""")]
    public void DecodeHello_BadProcessId_IsRejected(string json)
    {
        Assert.Null(HandoverProtocol.DecodeHello(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void DecodeStart_InvalidUtf8_IsRejected()
    {
        var message = Encoding.UTF8.GetBytes("""{"version":1,"type":"start","arguments":["x"]}""");
        message[^4] = 0xFF;

        Assert.Null(HandoverProtocol.DecodeStart(message));
    }

    [Fact]
    public void DecodeStart_TooManyArguments_IsRejected()
    {
        var items = string.Join(",", Enumerable.Repeat("\"a\"", HandoverProtocol.MaxArguments + 1));
        var json = $$"""{"version":1,"type":"start","arguments":[{{items}}]}""";

        Assert.Null(HandoverProtocol.DecodeStart(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void DecodeStart_DeepNesting_IsRejected()
    {
        var json = """{"version":1,"type":"start","arguments":[],"x":""" + new string('[', 100) + new string(']', 100) + "}";

        Assert.Null(HandoverProtocol.DecodeStart(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void EncodeStart_TooManyArguments_Throws()
    {
        Assert.Throws<ArgumentException>(() => HandoverProtocol.EncodeStart(Enumerable.Repeat("a", HandoverProtocol.MaxArguments + 1).ToArray()));
    }

    [Fact]
    public void EncodeStart_OverTheSizeCap_Throws()
    {
        Assert.Throws<ArgumentException>(() => HandoverProtocol.EncodeStart([new string('a', HandoverProtocol.MaxMessageBytes)]));
    }

    [Fact]
    public async Task WriteAndRead_RoundTrip()
    {
        using var stream = new MemoryStream();
        var message = HandoverProtocol.EncodeStart(["one", "two"]);

        await HandoverProtocol.WriteAsync(stream, message, CancellationToken.None);
        stream.Position = 0;

        Assert.Equal(message, await HandoverProtocol.ReadAsync(stream, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(HandoverProtocol.MaxMessageBytes + 1)]
    public async Task Read_LengthOutsideTheCap_Throws(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(() => HandoverProtocol.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Read_TruncatedMessage_Throws()
    {
        using var stream = new MemoryStream([10, 0, 0, 0, (byte)'{']);

        await Assert.ThrowsAsync<EndOfStreamException>(() => HandoverProtocol.ReadAsync(stream, CancellationToken.None));
    }
}
