using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Borea.Storage.Tests.Launch;

/// <summary>Writes a .NET DLL with no code that references the named assemblies, as a mod built for another game build does.</summary>
internal static class ReferencingAssembly
{
    public static void Write(string path, params string[] references)
    {
        var metadata = new MetadataBuilder();
        var name = Path.GetFileNameWithoutExtension(path);
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
        foreach (var reference in references)
            metadata.AddAssemblyReference(metadata.GetOrAddString(reference), new Version(2026, 9, 0, 0), default, default, 0, default);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var image = new BlobBuilder();
        new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), new BlobBuilder()).Serialize(image);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        image.WriteContentTo(file);
    }
}
