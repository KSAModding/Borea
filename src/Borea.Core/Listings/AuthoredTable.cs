namespace Borea.Core.Listings;

/// <summary>
/// One table of an authored document, with its keys in the order they were read or added.
/// A value is a string, a long, a double, a bool, another table, or a list of such values.
/// </summary>
public sealed class AuthoredTable
{
    private readonly List<KeyValuePair<string, object>> _entries = [];

    public IReadOnlyList<KeyValuePair<string, object>> Entries => _entries;

    public int Count => _entries.Count;

    public object? this[string key] => TryGet(key, out var value) ? value : null;

    public bool TryGet(string key, out object value)
    {
        var index = IndexOf(key);
        value = index < 0 ? null! : _entries[index].Value;
        return index >= 0;
    }

    public bool Contains(string key) => IndexOf(key) >= 0;

    /// <summary>Replaces the value in place when the key exists, and adds it at the end otherwise.</summary>
    public void Set(string key, object value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!IsValue(value))
            throw new ArgumentException($"A value of type {value?.GetType().Name ?? "null"} cannot be part of an authored document.", nameof(value));

        var entry = new KeyValuePair<string, object>(key, value);
        var index = IndexOf(key);
        if (index < 0)
            _entries.Add(entry);
        else
            _entries[index] = entry;
    }

    public bool Remove(string key)
    {
        var index = IndexOf(key);
        if (index < 0)
            return false;

        _entries.RemoveAt(index);
        return true;
    }

    public string? GetString(string key) => this[key] as string;

    public AuthoredTable? GetTable(string key) => this[key] as AuthoredTable;

    public IReadOnlyList<object>? GetList(string key) => this[key] as IReadOnlyList<object>;

    public AuthoredTable Clone()
    {
        var copy = new AuthoredTable();
        foreach (var (key, value) in _entries)
            copy._entries.Add(new KeyValuePair<string, object>(key, CloneValue(value)));
        return copy;
    }

    private static object CloneValue(object value) => value switch
    {
        AuthoredTable table => table.Clone(),
        IReadOnlyList<object> list => list.Select(CloneValue).ToList(),
        _ => value,
    };

    private static bool IsValue(object? value) => value switch
    {
        string or long or double or bool or AuthoredTable => true,
        IReadOnlyList<object> list => list.All(IsValue),
        _ => false,
    };

    private int IndexOf(string key) => _entries.FindIndex(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));
}
