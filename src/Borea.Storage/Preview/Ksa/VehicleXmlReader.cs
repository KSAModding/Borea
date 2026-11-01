using System.Numerics;
using System.Xml;
using Borea.Core.Preview;

namespace Borea.Storage.Preview.Ksa;

/// <summary>
/// One full part of a vehicle. <paramref name="Transform"/> takes the part from its own frame into the vehicle
/// assembly frame directly, because the Transform of a full part is global and does not compose with its tree parent.
/// <paramref name="ParentLocalId"/> is the LocalInstanceId of the part it nests in, or null for the root.
/// </summary>
internal sealed record VehiclePartRef(string InstanceOf, uint LocalInstanceId, Matrix4x4 Transform, uint? ParentLocalId = null);

/// <summary>
/// A FuelLink element of a vehicle. <paramref name="Nodes"/> are the points that the player placed on the hose, in the
/// frame of part A. A hose without any is the normal case, because the file has no Node element for such a link.
/// </summary>
internal sealed record VehicleFuelLink(uint PartA, uint PartB, IReadOnlyList<Vector3> Nodes);

/// <summary>
/// The full parts of a vehicle.xml in document order. When <paramref name="Problems"/> has an AssetFailed entry, the
/// file could not be read and <paramref name="Parts"/> is empty, because the game also loads nothing from a file
/// that XmlSerializer cannot read.
/// </summary>
internal sealed record VehicleXml(IReadOnlyList<VehiclePartRef> Parts, IReadOnlyList<PreviewNotice> Problems)
{
    public IReadOnlyList<VehicleFuelLink> FuelLinks { get; init; } = [];
}

/// <summary>
/// Reads the full parts and the fuel links of a vehicle.xml as VehicleSaveData and PartInstance declare them.
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
/// A fuel link with a number that cannot be read, a number that is not finite, or a node farther from part A than
/// <see cref="MaxNodeOffset"/> is left out, and the rest of the file stays. A far node would change the camera framing
/// of the whole vehicle.
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

    public const int MaxFuelLinks = 4_096;

    /// <summary>The most nodes of one fuel link that count.</summary>
    public const int MaxFuelLinkNodes = 60;

    /// <summary>The largest distance in meters from part A that a node can have.</summary>
    public const float MaxNodeOffset = 1000f;

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
            var links = new List<VehicleFuelLink>();
            var parts = ReadParts(stream, links, problems);
            return new VehicleXml(parts, problems) { FuelLinks = links };
        }
        catch (Exception exception) when (exception is XmlException or FormatException or OverflowException or IOException or InvalidDataException)
        {
            problems.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, FileName, exception.Message));
            return new VehicleXml([], problems);
        }
    }

    private static List<VehiclePartRef> ReadParts(Stream stream, List<VehicleFuelLink> links, List<PreviewNotice> problems)
    {
        using var reader = XmlReader.Create(stream, Settings);
        if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != KsaPartSchema.VehicleSaveData)
            throw new XmlException("The root element is not VehicleSaveData.");

        var parts = new List<PartBuilder>();

        // The depth and the index in parts of each open part element, innermost on top.
        var open = new Stack<(int Depth, int Index)>();
        var rootSeen = false;
        var overCap = false;
        var badLinks = 0;
        var linksOverCap = 0;

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
                uint? parentLocalId = isChild ? parts[open.Peek().Index].LocalInstanceId : null;
                parts.Add(new PartBuilder(reader.GetAttribute(KsaPartSchema.InstanceOf) ?? string.Empty, localId is null ? 0 : XmlConvert.ToUInt32(localId), parentLocalId));
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

            if (depth == 1 && name == KsaPartSchema.FuelLink)
            {
                if (links.Count >= MaxFuelLinks)
                {
                    linksOverCap++;
                    reader.Skip();
                    continue;
                }

                if (ReadFuelLink(reader) is { } link)
                    links.Add(link);
                else
                    badLinks++;

                reader.Read();
                continue;
            }

            reader.Skip();
        }

        if (!rootSeen)
            throw new XmlException("The file has no RootPartRef element.");

        if (badLinks > 0)
            problems.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, FileName, $"{badLinks} fuel lines have a number that cannot be used, so they are left out."));

        if (linksOverCap > 0)
            problems.Add(new PreviewNotice(PreviewNoticeKind.Budget, FileName, $"Only the first {MaxFuelLinks} fuel lines are read."));

        var result = new List<VehiclePartRef>(parts.Count);
        foreach (var part in parts)
        {
            var matrix = (part.Transform ?? KsaTransform.Identity).ToMatrix();
            if (KsaTransform.IsFinite(matrix))
                result.Add(new VehiclePartRef(part.InstanceOf, part.LocalInstanceId, matrix, part.ParentLocalId));
            else
                problems.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, FileName, $"Part {part.LocalInstanceId} ({part.InstanceOf}) has a Transform that is not a finite number, so it is left out."));
        }

        return result;
    }

    /// <summary>
    /// Reads the FuelLink element that the reader is on, and leaves the reader on its end tag, or on the element itself
    /// when it is empty. A missing part attribute is 0.
    /// </summary>
    /// <returns>The link, or null when a number cannot be used.</returns>
    private static VehicleFuelLink? ReadFuelLink(XmlReader reader)
    {
        using var subtree = reader.ReadSubtree();
        subtree.Read();
        try
        {
            var partA = subtree.GetAttribute(KsaPartSchema.PartA) is { } a ? XmlConvert.ToUInt32(a) : 0;
            var partB = subtree.GetAttribute(KsaPartSchema.PartB) is { } b ? XmlConvert.ToUInt32(b) : 0;
            var nodes = new List<Vector3>();
            while (subtree.Read())
            {
                if (subtree.NodeType != XmlNodeType.Element || subtree.Depth != 1 || subtree.LocalName != KsaPartSchema.Node || nodes.Count >= MaxFuelLinkNodes)
                    continue;

                var node = KsaTransform.ReadVector(subtree);

                // The inverted test also catches a NaN, because each compare with NaN is false.
                if (!(node.Length() <= MaxNodeOffset))
                    return null;

                nodes.Add(node);
            }

            return new VehicleFuelLink(partA, partB, nodes);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            return null;
        }
    }

    private sealed class PartBuilder(string instanceOf, uint localInstanceId, uint? parentLocalId)
    {
        public string InstanceOf { get; } = instanceOf;

        public uint LocalInstanceId { get; } = localInstanceId;

        public uint? ParentLocalId { get; } = parentLocalId;

        public KsaTransform? Transform { get; set; }
    }
}
