using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Storage.Index.Dtos;
using Borea.Storage.Paths;
using System.Reflection;
using System.Text.Json;
using Borea.Core.Paths;

namespace Borea.Storage.Index;

/// <summary>
/// This class is used to validate the data in the index.json file.
/// </summary>
public class IndexValidator
{
    private IGamePathProvider _pathProvider;

    public static readonly JsonSerializerOptions _jsonOptions = new()
    {
        RespectNullableAnnotations = true,
    };

    public IndexValidator(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    /// <summary>
    /// Validates the index.json file and returns the result of validation if it is valid.
    /// Throws an exception with the reason if it is not valid.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>"
    public IndexValidationResult ValidateIndex()
    {
        string indexPath = _pathProvider.GetIndexPath();

        string indexFile = ReadIndex(indexPath);

        if (string.IsNullOrEmpty(indexFile))
        {
            throw new InvalidOperationException($"Index file is empty at {indexPath}");
        }

        ContentIndexRootValidator.ValidateIndexRoot(indexFile, indexPath);

        return SnapshotParser.Parse(indexFile);
    }

    public string ReadIndex(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            throw new InvalidOperationException($"Index does not exist at {path}");
        }

        var indexFile = File.ReadAllText(path);

        return indexFile;
    }

    public void WriteIndex(string path, string content)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new InvalidOperationException($"Index path is null or empty");
        }
        File.WriteAllText(path, content);
    }
}
