using System.Xml.Linq;
using Borea.Core.Preview;

namespace Borea.Storage.Preview.Ksa;

/// <summary>
/// One top-level element of an asset file. <paramref name="Order"/> is its place in the load order of all files.
/// </summary>
internal sealed record AssetRow(XElement Element, string ModId, string FilePath, int Order)
{
    public string Tag => Element.Name.LocalName;

    public string Subject => $"{ModId}/{FilePath}";

    public string? Id => Element.Attribute(KsaPartSchema.Id)?.Value is { Length: > 0 } id ? id : null;
}

/// <summary>A row that a row without Modify left out, because <paramref name="First"/> has its tag and Id.</summary>
internal sealed record DuplicateRow(AssetRow Dropped, AssetRow First);

/// <summary>
/// The merged rows in the order of the rows that define them, the rows that are left out as duplicates, and the
/// notices of the merges that failed.
/// </summary>
internal sealed record MergedRows(IReadOnlyList<AssetRow> Rows, IReadOnlyList<DuplicateRow> Duplicates, IReadOnlyList<PreviewNotice> Notices);

/// <summary>
/// Merges the rows of the asset files into one element per tag and Id, as the game does before it reads a row into a
/// class (ContentPrePass.Resolve with ChildMerge).
/// </summary>
/// <remarks>
/// <para>
/// A row without Modify defines its tag and Id, and a later row with the same tag and Id and without Modify is left
/// out. Tags compare ordinal and Ids ignore case (RowKey). A row with Modify="Patch" merges into the row it names,
/// Replace takes its place and Remove takes it out, after which a row without Modify can define it again. The
/// IfExists forms do nothing when no such row is there. A row with Parent starts from the merged row of the same tag
/// that Parent names and merges its own content into it as a patch, and an Abstract row only serves as a parent.
/// </para>
/// <para>
/// The children merge as in ChildMerge: a child without Modify replaces the one child with its name or is added, Patch
/// merges into that child, Extend adds its items to the list with that name and matches each item that has its own
/// Modify by Key, or by Ref when it has no Key, and Replace and Remove act on the one child. A patch sets its attributes
/// on the target, but a row patch keeps the Id, Parent and Abstract of the target, and an item keeps the Key or Ref it
/// was found by. A patch that does not find its target leaves the row as it was before that patch, with a notice.
/// </para>
/// <para>
/// The game also checks each row against rules that this merge does not apply (ChildMerge.TryValidate, the reserved
/// attributes, unique Keys) and drops a row that breaks one. The preview keeps such a row, so that a file in the older
/// layout, which writes Id on nested elements and has no Modify, still gives its content. For the same reason a row
/// without an Id is not merged and stays as it is.
/// </para>
/// </remarks>
internal static class AssetRowMerge
{
    private enum RowVerb
    {
        None,
        Patch,
        Replace,
        Remove,
    }

    private enum ChildVerb
    {
        None,
        Patch,
        Extend,
        Replace,
        Remove,
    }

    private enum EntryState
    {
        Pending,
        Active,
        Done,
        Failed,
    }

    public static MergedRows Merge(IReadOnlyList<AssetRow> rows, CancellationToken cancellationToken)
    {
        var merge = new Merger();
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            merge.Index(row);
        }

        return merge.Flatten(cancellationToken);
    }

    private sealed class Entry(AssetRow defining)
    {
        public AssetRow Defining { get; } = defining;

        public List<(AssetRow Row, RowVerb Verb)> Steps { get; } = [];

        public bool Removed { get; set; }

        public EntryState State { get; set; }

        public XElement? Flat { get; set; }

        public bool IsAbstract { get; set; }
    }

    private sealed class Merger
    {
        private readonly Dictionary<(string Tag, string Id), Entry> _index = new(new RowKeyComparer());
        private readonly List<Entry> _entries = [];
        private readonly List<AssetRow> _withoutId = [];
        private readonly List<DuplicateRow> _duplicates = [];
        private readonly List<PreviewNotice> _notices = [];

        public void Index(AssetRow row)
        {
            var modify = row.Element.Attribute(KsaPartSchema.Modify)?.Value;
            if (!TryParseRowVerb(modify, out var verb, out var ifExists))
            {
                Fail(row, $"Modify=\"{modify}\" is not a verb for a row, so the row is left out.");
                return;
            }

            if (row.Id is not { } id)
            {
                if (verb == RowVerb.None)
                    _withoutId.Add(row);
                else
                    Fail(row, $"A {row.Tag} with Modify=\"{modify}\" has no Id, so it is left out.");
                return;
            }

            var key = (row.Tag, id);
            var target = _index.GetValueOrDefault(key);
            if (verb == RowVerb.None)
            {
                if (target is { Removed: false })
                {
                    _duplicates.Add(new DuplicateRow(row, target.Defining));
                    return;
                }

                var entry = new Entry(row);
                _index[key] = entry;
                _entries.Add(entry);
            }
            else if (target is null or { Removed: true })
            {
                if (!ifExists)
                    Fail(row, $"Modify=\"{modify}\" on {row.Tag} '{id}' finds no earlier {row.Tag} with this Id, so it is left out.");
            }
            else if (verb == RowVerb.Remove)
            {
                target.Removed = true;
            }
            else
            {
                target.Steps.Add((row, verb));
            }
        }

        public MergedRows Flatten(CancellationToken cancellationToken)
        {
            var rows = new List<AssetRow>(_entries.Count + _withoutId.Count);
            foreach (var entry in _entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entry.Removed && Flatten(entry) && !entry.IsAbstract)
                    rows.Add(entry.Defining with { Element = entry.Flat! });
            }

            rows.AddRange(_withoutId);
            rows.Sort((left, right) => left.Order.CompareTo(right.Order));
            return new MergedRows(rows, _duplicates, _notices);
        }

        /// <summary>Merges the entry once, after its parents. An entry whose parents lead back to it fails.</summary>
        private bool Flatten(Entry entry)
        {
            switch (entry.State)
            {
                case EntryState.Done:
                    return true;
                case EntryState.Failed:
                    return false;
                case EntryState.Active:
                    _notices.Add(new PreviewNotice(PreviewNoticeKind.Cycle, entry.Defining.Id!, $"The Parent chain of {entry.Defining.Tag} '{entry.Defining.Id}' in {entry.Defining.Subject} leads back to itself."));
                    entry.State = EntryState.Failed;
                    return false;
            }

            entry.State = EntryState.Active;
            var flat = BuildOver(entry, entry.Defining);
            if (entry.State == EntryState.Failed || flat is null)
            {
                entry.State = EntryState.Failed;
                return false;
            }

            var isAbstract = IsAbstract(entry.Defining.Element);
            foreach (var (row, verb) in entry.Steps)
            {
                if (verb == RowVerb.Replace)
                {
                    if (BuildOver(entry, row) is { } replaced)
                    {
                        flat = replaced;
                        isAbstract = IsAbstract(row.Element);
                    }
                }
                else if (ApplyPatch(flat, row) is { } patched)
                {
                    flat = patched;
                }
            }

            entry.Flat = flat;
            entry.IsAbstract = isAbstract;
            entry.State = EntryState.Done;
            return true;
        }

        /// <summary>The row as its own element, or merged over its parent when it names one.</summary>
        private XElement? BuildOver(Entry entry, AssetRow row)
        {
            var id = entry.Defining.Id!;
            if (row.Element.Attribute(KsaPartSchema.Parent)?.Value is not { } parentId)
            {
                var copy = Fresh(row.Element);
                copy.Attribute(KsaPartSchema.Abstract)?.Remove();
                copy.SetAttributeValue(KsaPartSchema.Id, id);
                return copy;
            }

            if (_index.GetValueOrDefault((entry.Defining.Tag, parentId)) is not { Removed: false } parent)
            {
                Fail(row, $"Parent=\"{parentId}\" of {row.Tag} '{id}' names no {row.Tag}, so the row is left out.");
                return null;
            }

            if (!Flatten(parent) || ApplyPatch(parent.Flat!, row) is not { } merged)
                return null;

            merged.SetAttributeValue(KsaPartSchema.Id, id);
            return merged;
        }

        /// <summary>A merged copy of <paramref name="flat"/>, or null with a notice when the patch does not apply.</summary>
        private XElement? ApplyPatch(XElement flat, AssetRow patch)
        {
            var copy = new XElement(flat);
            MergeAttributes(copy, patch.Element, isRow: true, matched: null);
            if (ApplyRecord(copy, patch.Element) is not { } problem)
                return copy;

            Fail(patch, $"The {patch.Tag} '{patch.Id}' is left as it was before this row, because {problem}.");
            return null;
        }

        private void Fail(AssetRow row, string detail) =>
            _notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, row.Subject, detail));
    }

    #region ChildMerge

    /// <summary>Applies each child of the patch to the target, and gives the first problem, or null.</summary>
    private static string? ApplyRecord(XElement target, XElement patch)
    {
        foreach (var child in patch.Elements().ToList())
        {
            if (!TryParseChildVerb(child, out var verb))
                return $"Modify=\"{child.Attribute(KsaPartSchema.Modify)!.Value}\" on <{child.Name.LocalName}> is not a verb for a child";

            if (ApplyChild(target, child, verb) is { } problem)
                return problem;
        }

        return null;
    }

    private static string? ApplyChild(XElement target, XElement child, ChildVerb verb)
    {
        var name = child.Name.LocalName;
        var matches = target.Elements(child.Name).Take(2).ToList();
        if (matches.Count > 1)
            return $"the target has more than one <{name}>";

        var existing = matches.FirstOrDefault();
        if (existing is null && verb is ChildVerb.Patch or ChildVerb.Replace or ChildVerb.Remove)
            return $"the target has no <{name}> to change";

        switch (verb)
        {
            case ChildVerb.None when existing is null:
                target.Add(Fresh(child));
                return null;
            case ChildVerb.None or ChildVerb.Replace:
                existing!.ReplaceWith(Fresh(child));
                return null;
            case ChildVerb.Remove:
                existing!.Remove();
                return null;
            case ChildVerb.Patch:
                return PatchInto(existing!, child, matched: null);
            default:
                if (existing is null)
                {
                    existing = new XElement(child.Name);
                    target.Add(existing);
                }

                return ExtendInto(existing, child, matched: null);
        }
    }

    private static string? PatchInto(XElement target, XElement patch, XName? matched)
    {
        MergeAttributes(target, patch, isRow: false, matched);
        if (patch.HasElements)
            return ApplyRecord(target, patch);

        ReplaceText(target, patch);
        return null;
    }

    private static string? ExtendInto(XElement container, XElement extend, XName? matched)
    {
        MergeAttributes(container, extend, isRow: false, matched);
        foreach (var item in extend.Elements().ToList())
        {
            if (!TryParseChildVerb(item, out var verb))
                return $"Modify=\"{item.Attribute(KsaPartSchema.Modify)!.Value}\" on <{item.Name.LocalName}> is not a verb for a child";

            if (verb != ChildVerb.None)
            {
                if (ApplyItem(container, item, verb) is { } problem)
                    return problem;
                continue;
            }

            if (item.Attribute(KsaPartSchema.Key)?.Value is { Length: > 0 } key && container.Elements().Any(existing => HasKey(existing, key)))
                return $"<{container.Name.LocalName}> already has an item with Key=\"{key}\"";

            container.Add(Fresh(item));
        }

        return null;
    }

    /// <summary>Applies an item with its own Modify to the one item of the list with the same Key, or the same Ref when it has no Key.</summary>
    private static string? ApplyItem(XElement container, XElement item, ChildVerb verb)
    {
        var (identity, value) = Identity(item);
        if (value.Length == 0)
            return $"<{item.Name.LocalName} Modify=\"{item.Attribute(KsaPartSchema.Modify)!.Value}\"> in an Extend has no Key or Ref";

        var byRef = identity == KsaPartSchema.Ref;
        var matches = container.Elements()
            .Where(existing => !(byRef && existing.Attribute(KsaPartSchema.Key)?.Value is { Length: > 0 }))
            .Where(existing => string.Equals(existing.Attribute(identity)?.Value, value, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        if (matches.Count > 1)
            return $"<{container.Name.LocalName}> has more than one item with {identity}=\"{value}\"";
        if (matches.Count == 0)
            return $"<{container.Name.LocalName}> has no item with {identity}=\"{value}\"";

        var existing = matches[0];
        if (verb is ChildVerb.Patch or ChildVerb.Extend && existing.Name != item.Name)
            return $"{identity}=\"{value}\" names a <{existing.Name.LocalName}>, not a <{item.Name.LocalName}>";

        switch (verb)
        {
            case ChildVerb.Patch:
                return PatchInto(existing, item, identity);
            case ChildVerb.Extend:
                return ExtendInto(existing, item, identity);
            case ChildVerb.Replace:
                var replacement = Fresh(item);
                replacement.SetAttributeValue(identity, existing.Attribute(identity)!.Value);
                existing.ReplaceWith(replacement);
                return null;
            default:
                existing.Remove();
                return null;
        }
    }

    /// <summary>Sets the attributes of the patch on the target. A row keeps its Id, Parent and Abstract, and an item keeps the attribute it was found by.</summary>
    private static void MergeAttributes(XElement target, XElement patch, bool isRow, XName? matched)
    {
        foreach (var attribute in patch.Attributes())
        {
            var name = attribute.Name.LocalName;
            if (name == KsaPartSchema.Modify)
                continue;
            if (isRow && name is KsaPartSchema.Id or KsaPartSchema.Parent or KsaPartSchema.Abstract)
                continue;
            if (attribute.Name == matched)
                continue;

            target.SetAttributeValue(attribute.Name, attribute.Value);
        }
    }

    /// <summary>Puts the text of a patch without children in place of the text of the target. An empty text changes nothing.</summary>
    private static void ReplaceText(XElement target, XElement patch)
    {
        var text = patch.Value;
        if (string.IsNullOrWhiteSpace(text))
            return;

        target.Nodes().OfType<XText>().ToList().ForEach(node => node.Remove());
        target.AddFirst(new XText(text));
    }

    /// <summary>A copy of the element without any Modify attribute, as content to add.</summary>
    private static XElement Fresh(XElement element)
    {
        var copy = new XElement(element);
        foreach (var descendant in copy.DescendantsAndSelf())
            descendant.Attribute(KsaPartSchema.Modify)?.Remove();
        return copy;
    }

    private static (XName Name, string Value) Identity(XElement item)
    {
        if (item.Attribute(KsaPartSchema.Key)?.Value is { Length: > 0 } key)
            return (KsaPartSchema.Key, key);

        return item.Attribute(KsaPartSchema.Ref)?.Value is { Length: > 0 } reference
            ? (KsaPartSchema.Ref, reference)
            : (KsaPartSchema.Key, string.Empty);
    }

    private static bool HasKey(XElement element, string key) =>
        string.Equals(element.Attribute(KsaPartSchema.Key)?.Value, key, StringComparison.OrdinalIgnoreCase);

    #endregion

    #region Verbs

    /// <summary>The verbs of a row as ModifyVerbs.TryParseName reads them, ignoring case, and whether each is an IfExists form.</summary>
    private static readonly Dictionary<string, (RowVerb Verb, bool IfExists)> RowVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Patch"] = (RowVerb.Patch, false),
        ["Replace"] = (RowVerb.Replace, false),
        ["Remove"] = (RowVerb.Remove, false),
        ["PatchIfExists"] = (RowVerb.Patch, true),
        ["ReplaceIfExists"] = (RowVerb.Replace, true),
        ["RemoveIfExists"] = (RowVerb.Remove, true),
    };

    /// <summary>The verbs of a child as ModifyVerbs.TryParseChild reads them, ignoring case.</summary>
    private static readonly Dictionary<string, ChildVerb> ChildVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Patch"] = ChildVerb.Patch,
        ["Extend"] = ChildVerb.Extend,
        ["Replace"] = ChildVerb.Replace,
        ["Remove"] = ChildVerb.Remove,
    };

    /// <summary>The verb of a row. A row without Modify has None.</summary>
    private static bool TryParseRowVerb(string? modify, out RowVerb verb, out bool ifExists)
    {
        (verb, ifExists) = (RowVerb.None, false);
        if (modify is null)
            return true;
        if (!RowVerbs.TryGetValue(modify, out var parsed))
            return false;

        (verb, ifExists) = parsed;
        return true;
    }

    /// <summary>The verb of a child. A child without Modify has None.</summary>
    private static bool TryParseChildVerb(XElement element, out ChildVerb verb)
    {
        verb = ChildVerb.None;
        return element.Attribute(KsaPartSchema.Modify)?.Value is not { } modify || ChildVerbs.TryGetValue(modify, out verb);
    }

    private static bool IsAbstract(XElement row) =>
        string.Equals(row.Attribute(KsaPartSchema.Abstract)?.Value, "true", StringComparison.OrdinalIgnoreCase);

    #endregion

    /// <summary>The RowKey of the game: the tag compares ordinal, the Id ignores case.</summary>
    private sealed class RowKeyComparer : IEqualityComparer<(string Tag, string Id)>
    {
        public bool Equals((string Tag, string Id) left, (string Tag, string Id) right) =>
            string.Equals(left.Tag, right.Tag, StringComparison.Ordinal) && string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Tag, string Id) key) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(key.Tag), StringComparer.OrdinalIgnoreCase.GetHashCode(key.Id));
    }
}
