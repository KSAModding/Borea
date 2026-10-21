using System.Numerics;
using System.Xml;
using Borea.Core.Preview;

namespace Borea.Storage.Preview.Ksa;

/// <summary>
/// One full part of a vehicle. <paramref name="Transform"/> takes the part from its own frame into the vehicle
/// assembly frame directly, because the Transform of a full part is global and does not compose with its tree parent.
/// </summary>
internal sealed record VehiclePartRef(string InstanceOf, uint LocalInstanceId, Matrix4x4 Transform);

/// <summary>
/// The full parts of a vehicle.xml in document order. When <paramref name="Problems"/> has an AssetFailed entry, the
/// file could not be read and <paramref name="Parts"/> is empty, because the game also loads nothing from a file
/// that XmlSerializer cannot read.
/// </summary>
internal sealed record VehicleXml(IReadOnlyList<VehiclePartRef> Parts, IReadOnlyList<PreviewNotice> Problems);

/// <summary>
/// Reads the full parts of a vehicle.xml as VehicleSaveData and PartInstance declare them.
/// </summary>
/// <remarks>
/// <para>
/// The tree starts at the RootPartRef element (VehicleSaveData.RootPartInstance), and each PartRef element directly
/// inside a part is a child part (PartInstance.Children). A PartRef inside SymmetryRef/Layer is only the
/// LocalInstanceId of a part as text (SymmetryLayerInstance.PartLocalInstanceIds), so only a PartRef with an
/// InstanceOf attribute counts. SubPartRef holds the save data of the subparts that the template already lists, and
/// PartConnectorRef holds connections without a position, so the preview reads neither.
/// </para>
/// <para>
/// A full part has no PartParent in the game, so Part.MatrixAsmb2VehicleAsmb is its MatrixAsmb2ParentAsmb, built from
/// its own Transform (the Part constructor calls inInstance.Transform.Create). A PartRef without a Transform gets the
/// identity, as the Part fields default to a zero position, the identity rotation and a scale of one.
/// </para>
/// <para>
/// The reader walks the file with a stack and not with recursion, because a vehicle can nest thousands of parts.
/// </para>
/// </remarks>
internal static class VehicleXmlReader
{
    public const string FileName = "vehicle.xml";

    /// <summary>The largest vehicle.xml the preview reads, in characters. The stock vehicles are far below 1 M.</summary>
    public const long MaxCharacters = 16L * 1024 * 1024;

    public const int MaxParts = 10_000;

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxCharacters,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
    };

    public static VehicleXml Read(Stream stream)
    {
        var problems = new List<PreviewNotice>();
        try
        {
            var parts = ReadParts(stream, problems);
            return new VehicleXml(parts, problems);
        }
        catch (Exception exception) when (exception is XmlException or FormatException or OverflowException or IOException or InvalidDataException)
        {
            problems.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, FileName, exception.Message));
            return new VehicleXml([], problems);
        }
    }

    private static List<VehiclePartRef> ReadParts(Stream stream, List<PreviewNotice> problems)
    {
        using var reader = XmlReader.Create(stream, Settings);
        if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != KsaPartSchema.VehicleSaveData)
            throw new XmlException("The root element is not VehicleSaveData.");

        var parts = new List<PartBuilder>();

        // The depth and the index in parts of each open part element, innermost on top.
        var open = new Stack<(int Depth, int Index)>();
        var rootSeen = false;
        var overCap = false;

        reader.Read();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (open.Count > 0 && open.Peek().Depth == reader.Depth)
                    open.Pop();
                reader.Read();
                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            var depth = reader.Depth;
            var parentIsPart = open.Count > 0 && open.Peek().Depth == depth - 1;
            var name = reader.LocalName;

            var isRoot = depth == 1 && !rootSeen && name == KsaPartSchema.RootPartRef;
            var isChild = parentIsPart && name == KsaPartSchema.PartRef && reader.GetAttribute(KsaPartSchema.InstanceOf) is not null;
            if (isRoot || isChild)
            {
                rootSeen |= isRoot;
                if (parts.Count >= MaxParts)
                {
                    if (!overCap)
                        problems.Add(new PreviewNotice(PreviewNoticeKind.Budget, FileName, $"Only the first {MaxParts} parts are read."));
                    overCap = true;
                    reader.Skip();
                    continue;
                }

                var localId = reader.GetAttribute(KsaPartSchema.LocalInstanceId);
                parts.Add(new PartBuilder(reader.GetAttribute(KsaPartSchema.InstanceOf) ?? string.Empty, localId is null ? 0 : XmlConvert.ToUInt32(localId)));
                if (!reader.IsEmptyElement)
                    open.Push((depth, parts.Count - 1));
                reader.Read();
                continue;
            }

            if (parentIsPart && name == KsaPartSchema.Transform && parts[open.Peek().Index].Transform is null)
            {
                parts[open.Peek().Index].Transform = KsaTransform.Parse(reader);
                reader.Read();
                continue;
            }

            reader.Skip();
        }

        if (!rootSeen)
            throw new XmlException("The file has no RootPartRef element.");

        var result = new List<VehiclePartRef>(parts.Count);
        foreach (var part in parts)
        {
            var matrix = (part.Transform ?? KsaTransform.Identity).ToMatrix();
            if (KsaTransform.IsFinite(matrix))
                result.Add(new VehiclePartRef(part.InstanceOf, part.LocalInstanceId, matrix));
            else
                problems.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, FileName, $"Part {part.LocalInstanceId} ({part.InstanceOf}) has a Transform that is not a finite number, so it is left out."));
        }

        return result;
    }

    private sealed class PartBuilder(string instanceOf, uint localInstanceId)
    {
        public string InstanceOf { get; } = instanceOf;

        public uint LocalInstanceId { get; } = localInstanceId;

        public KsaTransform? Transform { get; set; }
    }
}
