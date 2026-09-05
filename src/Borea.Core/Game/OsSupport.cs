using System.Collections.ObjectModel;

namespace Borea.Core.Game;

public sealed class OsSupport
{
    public bool IsSupported { get; }

    public IReadOnlyList<string> Unrecognized { get; }

    public OsSupport(bool isSupported, IReadOnlyList<string>? unrecognized = null)
    {
        IsSupported = isSupported;
        Unrecognized = unrecognized is null
            ? Array.Empty<string>()
            : new ReadOnlyCollection<string>(unrecognized.ToArray());
    }
}
