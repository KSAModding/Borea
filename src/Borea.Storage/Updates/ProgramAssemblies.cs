using System.Reflection;

namespace Borea.Storage.Updates;

/// <summary>
/// A published Borea is one file, and it reads an assembly out of that file the first time the
/// assembly is used. An update moves the file aside and puts the new build at its path, so the
/// running build loads what it references first, and nothing it needs afterwards comes from there.
/// </summary>
internal static class ProgramAssemblies
{
    /// <summary>Loads every assembly <paramref name="root"/> references, directly or through another one.</summary>
    /// <returns>The names of the assemblies that are loaded now, <paramref name="root"/> included.</returns>
    public static IReadOnlySet<string> LoadAll(Assembly root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.GetName().Name! };
        var tried = new HashSet<string>(loaded, StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<Assembly>([root]);
        while (pending.TryPop(out var assembly))
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name is null || !tried.Add(reference.Name))
                    continue;

                try
                {
                    pending.Push(Assembly.Load(reference));
                    loaded.Add(reference.Name);
                }
                catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    // a reference the build does not ship, such as one for another system, is never used either
                }
            }
        }

        return loaded;
    }
}
