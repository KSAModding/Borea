using System.Xml;
using Borea.Core.Instances;
using Borea.Core.Sharing;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Instances;

/// <summary>
/// Reads the ids of the table "The content a file names" of research/vehicles-and-saves.md from vehicle.xml or universe.xml.
/// Module elements are left out, because the game drops a module whose template matches nothing (ModuleList.ApplySaveData),
/// and its template comes with the part.
/// </summary>
public sealed class XmlContentIdReader : IContentIdReader
{
    /// <summary>The file VehicleSaveData writes.</summary>
    public const string VehicleFileName = "vehicle.xml";

    /// <summary>The file UniverseData writes.</summary>
    public const string SaveFileName = "universe.xml";

    private static readonly HashSet<string> PartElements = new(StringComparer.Ordinal) { "RootPartRef", "PartRef", "SubPartRef" };

    private static readonly HashSet<string> CharacterElements = new(StringComparer.Ordinal) { "Vehicle", "Kitten" };

    private static readonly EnumerationOptions EveryFile = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public static string GameFileName(GameSaveKind kind) => kind == GameSaveKind.Save ? SaveFileName : VehicleFileName;

    public Task<GameFolderContent> ReadAsync(string folder, GameSaveKind kind, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        return Task.Run(() => ReadCoreAsync(folder, kind, cancellationToken), cancellationToken);
    }

    private static async Task<GameFolderContent> ReadCoreAsync(string folder, GameSaveKind kind, CancellationToken cancellationToken)
    {
        var metadata = Path.Combine(folder, FileGameSaveStore.MetadataFileName);
        if (!UserContentFolder.HasFile(folder, FileGameSaveStore.MetadataFileName))
            throw new GameFolderException(GameFolderProblem.MetadataMissing, metadata);

        var (name, build) = await ReadMetadataAsync(metadata, cancellationToken).ConfigureAwait(false);

        var gameFile = Path.Combine(folder, GameFileName(kind));
        if (!UserContentFolder.HasFile(folder, GameFileName(kind)))
            throw new GameFolderException(GameFolderProblem.GameFileMissing, gameFile);

        var ids = new ContentIds();
        var clutter = new List<GroundClutterRef>();
        try
        {
            await ReadGameFileAsync(gameFile, kind, ids, clutter, cancellationToken).ConfigureAwait(false);
        }
        catch (XmlException exception)
        {
            throw new GameFolderException(GameFolderProblem.GameFileNotWellFormed, gameFile, exception.Message, exception);
        }

        return new GameFolderContent(name, build, ids, clutter, ExtraFiles(folder, GameFileName(kind)));
    }

    private static async Task<(string Name, string? Build)> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new GameFolderException(GameFolderProblem.MetadataMissing, path, innerException: exception);
        }

        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException exception)
        {
            throw new GameFolderException(GameFolderProblem.MetadataUnreadable, path, exception.Message, exception);
        }

        if (!table.TryGetValue("name", out var value) || value is not string { Length: > 0 } name)
            throw new GameFolderException(GameFolderProblem.NameMissing, path);

        return (name, table.TryGetValue("version", out var version) && version is string { Length: > 0 } build ? build : null);
    }

    private static async Task ReadGameFileAsync(string path, GameSaveKind kind, ContentIds ids, List<GroundClutterRef> clutter, CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        using var reader = XmlReader.Create(stream, settings);

        // The names of the open elements, from the root down.
        var open = new List<string>();
        string? celestial = null;
        string? ecotype = null;
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    var element = reader.LocalName;
                    if (PartElements.Contains(element))
                        ids.Add(ContentIdKind.Part, reader.GetAttribute("InstanceOf"));
                    ids.Add(ContentIdKind.SubstancePhase, reader.GetAttribute("SubstancePhaseId"));
                    if (CharacterElements.Contains(element) || (open.Count == 0 && kind == GameSaveKind.Vehicle))
                        ids.Add(ContentIdKind.Character, reader.GetAttribute("Character"));
                    if (element == "ParentBody")
                        ids.Add(ContentIdKind.Body, reader.GetAttribute("Id"));
                    if (element == "Id" && open is [_, "System"])
                        ids.Add(ContentIdKind.System, reader.GetAttribute("Id"));
                    if (element == "GroundClutter")
                        (celestial, ecotype) = (null, null);

                    if (!reader.IsEmptyElement)
                        open.Add(element);
                    else if (element == "GroundClutter")
                        Close(element);
                    break;
                case XmlNodeType.Text or XmlNodeType.CDATA when open is [.., "GroundClutter", var child]:
                    if (child == "Celestial")
                        celestial = reader.Value.Trim();
                    else if (child == "Ecotype")
                        ecotype = reader.Value.Trim();
                    break;
                case XmlNodeType.EndElement:
                    Close(reader.LocalName);
                    open.RemoveAt(open.Count - 1);
                    break;
            }
        }

        void Close(string element)
        {
            if (element != "GroundClutter" || string.IsNullOrEmpty(celestial))
                return;

            ids.Add(ContentIdKind.Body, celestial);
            if (!string.IsNullOrEmpty(ecotype))
                clutter.Add(new GroundClutterRef(celestial, ecotype));
        }
    }

    /// <summary>Every file other than meta.toml and the game file at the root, which are the only files the game writes.</summary>
    private static List<string> ExtraFiles(string folder, string gameFile) =>
        new DirectoryInfo(folder).EnumerateFiles("*", EveryFile)
            .Select(file => Path.GetRelativePath(folder, file.FullName).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => path != FileGameSaveStore.MetadataFileName && path != gameFile)
            .Order(StringComparer.Ordinal)
            .ToList();
}
