using Borea.Core.Listings;
using Borea.Core.Mods;

namespace Borea.Core.Sharing;

public enum ContentShareNoteKind
{
    /// <summary>No mod folder of the instance defines the id.</summary>
    NotProvided,

    /// <summary>Only a mod the game does not load defines the id.</summary>
    DisabledProvider,

    /// <summary>A loaded mod defines the id, but its folder name is not a listing id, so no entry can name it.</summary>
    InvalidFolder,

    /// <summary>Two or more loaded mods define the id. The game keeps the first of them (SerializedCollection.Register).</summary>
    SharedId,

    /// <summary>A file of a mod could not be read, so the ids it defines are not known.</summary>
    UnreadableFile,
}

/// <param name="Ids">The ids the note is about.</param>
/// <param name="Mods">The mod folders the note names, in load order.</param>
/// <param name="Path">The file of an <see cref="ContentShareNoteKind.UnreadableFile"/> note.</param>
public sealed record ContentShareNote(ContentShareNoteKind Kind, IReadOnlyList<ContentId> Ids, IReadOnlyList<string> Mods, string? Path = null);

/// <param name="Dependencies">One required entry per mod that defines an id the file names, in load order.</param>
public sealed record ContentShare(IReadOnlyList<ListingDependency> Dependencies, IReadOnlyList<ContentShareNote> Notes);

/// <summary>
/// The dependencies a client proposes for a shared vehicle or save (RFC 0085, Dependencies): the mods that define the ids the file
/// names. It never adds the dependencies of those mods, and a mod that defines nothing the file names is not proposed.
/// </summary>
public static class ContentShareProposal
{
    /// <param name="installed">The mods Borea installed in the instance, whose version becomes min.</param>
    public static ContentShare Of(ContentIds used, ContentProviders providers, IReadOnlyList<InstalledMod> installed)
    {
        ArgumentNullException.ThrowIfNull(used);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(installed);

        var needed = new HashSet<ContentProvider>(ReferenceEqualityComparer.Instance);
        var notes = new List<ContentShareNote>();
        var disabled = new Dictionary<ContentProvider, List<ContentId>>(ReferenceEqualityComparer.Instance);
        var invalid = new Dictionary<ContentProvider, List<ContentId>>(ReferenceEqualityComparer.Instance);
        foreach (var id in used)
        {
            var definers = providers.Mods.Where(mod => mod.Defines.Contains(id)).ToList();

            // The game's own content needs no entry, even when a mod defines the id too.
            if (definers.Any(mod => mod.IsGameContent))
                continue;

            var loaded = definers.Where(mod => mod.Enabled).ToList();
            if (loaded.Count == 0)
            {
                if (definers.Count == 0)
                    notes.Add(new ContentShareNote(ContentShareNoteKind.NotProvided, [id], []));
                foreach (var mod in definers)
                    Add(disabled, mod, id);
                continue;
            }

            if (loaded.Count > 1)
                notes.Add(new ContentShareNote(ContentShareNoteKind.SharedId, [id], loaded.Select(mod => mod.FolderName).ToList()));
            foreach (var mod in loaded)
            {
                if (ModIds.IsValid(mod.FolderName))
                    needed.Add(mod);
                else
                    Add(invalid, mod, id);
            }
        }

        notes.AddRange(Grouped(ContentShareNoteKind.DisabledProvider, disabled, providers));
        notes.AddRange(Grouped(ContentShareNoteKind.InvalidFolder, invalid, providers));
        notes.AddRange(providers.UnreadableFiles.Select(path => new ContentShareNote(ContentShareNoteKind.UnreadableFile, [], [], path)));

        var dependencies = providers.Mods
            .Where(needed.Contains)
            .Select(mod => new ListingDependency(mod.FolderName, ListingDependencyChoices.Required, installed.FirstOrDefault(own => ModIds.Equals(own.ModId, mod.FolderName))?.Version.ToString()))
            .ToList();
        return new ContentShare(dependencies, notes);
    }

    private static void Add(Dictionary<ContentProvider, List<ContentId>> notes, ContentProvider mod, ContentId id)
    {
        if (!notes.TryGetValue(mod, out var ids))
            notes[mod] = ids = [];
        ids.Add(id);
    }

    private static IEnumerable<ContentShareNote> Grouped(ContentShareNoteKind kind, Dictionary<ContentProvider, List<ContentId>> notes, ContentProviders providers) =>
        providers.Mods.Where(notes.ContainsKey).Select(mod => new ContentShareNote(kind, notes[mod], [mod.FolderName]));
}
