using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Borea.Core.Listings;
using Json.Schema;

namespace Borea.Storage.Listings;

/// <summary>
/// Runs the authored schema of content-index, and says each rejection in words the way check_schema.py does:
/// a pattern by the title or example of its schema, a forbidden key as a key that is not allowed here.
/// </summary>
internal sealed class ListingSchemaCheck
{
    private static readonly HashSet<string> Branches = new(StringComparer.Ordinal) { "if", "not", "anyOf", "oneOf" };

    private readonly JsonSchema _schema;
    private readonly JsonNode _schemaNode;

    private ListingSchemaCheck(JsonSchema schema, JsonNode schemaNode)
    {
        _schema = schema;
        _schemaNode = schemaNode;
    }

    /// <exception cref="JsonException">The text is not JSON.</exception>
    /// <exception cref="JsonSchemaException">The text is not a schema of draft 2020-12 that loads.</exception>
    /// <exception cref="ArgumentException">A pattern of the schema is not a regular expression.</exception>
    public static ListingSchemaCheck Load(string text)
    {
        var node = JsonNode.Parse(text) ?? throw new JsonException("The schema is empty.");
        if (Text(Member(node, "$schema")) != "https://json-schema.org/draft/2020-12/schema")
            throw new JsonSchemaException("The schema does not declare draft 2020-12.");

        var schema = JsonSchema.FromText(text, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        return new ListingSchemaCheck(schema, node);
    }

    public IReadOnlyList<ListingIssue> Check(JsonNode document)
    {
        var results = _schema.Evaluate(JsonSerializer.SerializeToElement(document), new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (results.IsValid)
            return [];

        var issues = new List<ListingIssue>();
        foreach (var node in results.Details ?? [])
        {
            if (node.IsValid || node.Errors is null)
                continue;

            var evaluationPath = Segments(node.EvaluationPath.ToString());
            if (evaluationPath.Any(Branches.Contains))
                continue;

            var instancePath = Segments(node.InstanceLocation.ToString());
            var schema = Resolve(evaluationPath);
            var instance = Find(document, instancePath);
            foreach (var keyword in node.Errors.Keys)
            {
                if (Explain(keyword, schema, instance, instancePath) is { } issue)
                    issues.Add(issue);
            }
        }

        return issues.Distinct().ToList();
    }

    private static ListingIssue? Explain(string keyword, JsonNode? schema, JsonNode? instance, IReadOnlyList<string> path)
    {
        var value = Member(schema, keyword);
        string? message = keyword switch
        {
            "" when path.Count > 0 => $"'{path[^1]}' is not a known key here",
            "type" => $"{Show(instance)} is not {Article(value?.ToString() ?? "value")}",
            "enum" => $"{Show(instance)} is not one of {string.Join(", ", (value as JsonArray ?? []).Select(Show))}",
            "const" => $"{Show(instance)} must be {Show(value)}",
            "pattern" => Pattern(schema, instance),
            "minLength" => Number(value) == 1 ? "cannot be empty" : $"is shorter than {Number(value)} characters",
            "minItems" => Number(value) == 1 ? "needs at least one entry" : $"needs at least {Number(value)} entries",
            "maxItems" => $"has more than {Number(value)} entries",
            "minimum" => $"{Show(instance)} is less than the minimum of {Number(value)}",
            "maximum" => $"{Show(instance)} is greater than the maximum of {Number(value)}",
            "uniqueItems" => "names an entry more than once",
            "minProperties" => "needs at least one key",
            "not" => Not(value, instance),
            "anyOf" => Alternatives(value, instance, "needs one of {0}", "matches none of the forms allowed here"),
            "oneOf" => Alternatives(value, instance, "needs exactly one of {0}", "matches none or several of the forms allowed here"),
            _ => null,
        };

        if (keyword == "" && path.Count > 0)
            return new ListingIssue(ListingIssueSeverity.Error, Location(path.Take(path.Count - 1).ToList()), message!);

        if (keyword == "required")
            return Required(schema, instance, path);

        if (keyword == "dependentRequired")
            return DependentRequired(value, instance, path);

        return message is null ? null : new ListingIssue(ListingIssueSeverity.Error, Location(path), message);
    }

    private static ListingIssue? Required(JsonNode? schema, JsonNode? instance, IReadOnlyList<string> path)
    {
        var missing = (Member(schema, "required") as JsonArray ?? [])
            .Select(name => name?.GetValue<string>())
            .Where(name => name is not null && instance is JsonObject target && !target.ContainsKey(name))
            .ToList();
        return missing.Count == 0
            ? null
            : new ListingIssue(ListingIssueSeverity.Error, Location(path), string.Join("; ", missing.Select(name => $"'{name}' is a required property")));
    }

    private static ListingIssue? DependentRequired(JsonNode? value, JsonNode? instance, IReadOnlyList<string> path)
    {
        if (value is not JsonObject rules || instance is not JsonObject target)
            return null;

        var missing = rules
            .Where(rule => target.ContainsKey(rule.Key))
            .SelectMany(rule => (rule.Value as JsonArray ?? []).Select(name => (Key: rule.Key, Needed: name?.GetValue<string>())))
            .Where(pair => pair.Needed is not null && !target.ContainsKey(pair.Needed))
            .Select(pair => $"'{pair.Needed}' is required when '{pair.Key}' is set")
            .ToList();
        return missing.Count == 0 ? null : new ListingIssue(ListingIssueSeverity.Error, Location(path), string.Join("; ", missing));
    }

    private static string Pattern(JsonNode? schema, JsonNode? instance)
    {
        if (Member(schema, "examples") is JsonArray { Count: > 0 } examples)
            return $"{Show(instance)} does not have the right form; for example, use {Show(examples[0])}";

        return Text(Member(schema, "title")) is { } title
            ? $"{Show(instance)} is not {title}"
            : $"{Show(instance)} does not have the right form";
    }

    private static string Not(JsonNode? value, JsonNode? instance)
    {
        if (value is JsonObject { Count: 0 })
            return "this key is not allowed here";

        return Text(Member(value, "title")) is { } title
            ? $"{Show(instance)} is {title}"
            : $"{Show(instance)} is not allowed here";
    }

    /// <summary>Alternatives that each only require a key are said as the keys they name, which is what the schema uses them for.</summary>
    private static string Alternatives(JsonNode? value, JsonNode? instance, string keysFormat, string otherwise)
    {
        var keys = new List<string>();
        foreach (var branch in value as JsonArray ?? [])
        {
            if (branch is not JsonObject { Count: 1 } only || only["required"] is not JsonArray { Count: 1 } required)
                return otherwise;
            keys.Add($"'{required[0]!.GetValue<string>()}'");
        }

        if (instance is JsonObject target && keys.Count(key => target.ContainsKey(key.Trim('\''))) > 1)
            return $"can have only one of {string.Join(" or ", keys)}";

        return string.Format(CultureInfo.InvariantCulture, keysFormat, string.Join(" or ", keys));
    }

    /// <summary>The schema node an evaluation path reaches, following local references. A segment the node does not have is passed over.</summary>
    private JsonNode? Resolve(IReadOnlyList<string> evaluationPath)
    {
        var node = _schemaNode;
        foreach (var segment in evaluationPath)
        {
            if (segment == "$ref" && Text(Member(node, "$ref")) is { } reference && reference.StartsWith("#/", StringComparison.Ordinal))
            {
                node = Find(_schemaNode, Segments(reference[1..]));
                continue;
            }

            var next = node switch
            {
                JsonObject table when table.TryGetPropertyValue(segment, out var child) => child,
                JsonArray array when int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < array.Count => array[index],
                _ => null,
            };
            if (next is not null)
                node = next;
        }

        return node;
    }

    private static JsonNode? Find(JsonNode? root, IReadOnlyList<string> path)
    {
        var node = root;
        foreach (var segment in path)
        {
            node = node switch
            {
                JsonObject table => table[segment],
                JsonArray array when int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < array.Count => array[index],
                _ => null,
            };
        }

        return node;
    }

    private static List<string> Segments(string pointer) =>
        pointer.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(segment => segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToList();

    /// <summary>The location the way the document reads, such as images.description[0].url, as check_schema.py writes it.</summary>
    internal static string Location(IReadOnlyList<string> path)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var segment in path)
        {
            if (int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                builder.Append('[').Append(segment).Append(']');
            else
                builder.Append(builder.Length == 0 ? string.Empty : ".").Append(segment);
        }

        return builder.ToString();
    }

    private static string Show(JsonNode? value) => value switch
    {
        null => "nothing",
        JsonValue text when text.TryGetValue<string>(out var content) => $"'{content}'",
        _ => value.ToJsonString(),
    };

    private static JsonNode? Member(JsonNode? node, string key) => node is JsonObject table && table.TryGetPropertyValue(key, out var value) ? value : null;

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static long? Number(JsonNode? value) => value is JsonValue number && number.TryGetValue<long>(out var result) ? result : null;

    private static string Article(string type) => type switch
    {
        "array" or "object" or "integer" => $"an {type}",
        _ => $"a {type}",
    };
}
