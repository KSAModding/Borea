using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Borea.App.Localization;

public sealed record TranslatorCredit(string Name, string? Url = null)
{
    public bool HasUrl => Url is not null;
}

public sealed record LanguageTranslatorCredits(SupportedCulture Language, IReadOnlyList<TranslatorCredit> Translators)
{
    public bool IsOriginalLanguage => Language.Name == "en" && Translators.Count == 0;

    public bool IsUncredited => Language.Name != "en" && Translators.Count == 0;
}

/// <summary>Names are metadata, kept separate from the translated interface text.</summary>
internal static class TranslatorCreditsCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<TranslatorCredit>>> Entries = new(ReadEmbedded);

    internal static IReadOnlyList<LanguageTranslatorCredits> ForLanguages(IEnumerable<SupportedCulture> languages) =>
        languages.Select(language => new LanguageTranslatorCredits(language,
            Entries.Value.TryGetValue(language.Name, out var names) ? names : Array.Empty<TranslatorCredit>())).ToArray();

    internal static IReadOnlyDictionary<string, IReadOnlyList<TranslatorCredit>> Read(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Translator credits must be a JSON object.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = new Dictionary<string, IReadOnlyList<TranslatorCredit>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            var culture = entry.Name;
            var names = entry.Value.Deserialize<TranslatorCredit[]>(options);
            if (string.IsNullOrWhiteSpace(culture) || names is null)
                throw new InvalidDataException("A language credit needs a culture name and a list of translators.");

            foreach (var name in names)
            {
                if (name is null || string.IsNullOrWhiteSpace(name.Name))
                    throw new InvalidDataException($"A translator name is missing for {culture}.");
                if (name.Url is { } url && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
                    throw new InvalidDataException($"A translator link must be an absolute HTTPS URL: {culture}.");
            }

            if (!result.TryAdd(culture, Array.AsReadOnly(names)))
                throw new InvalidDataException($"Translator credits repeat the culture {culture}.");
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<TranslatorCredit>> ReadEmbedded()
    {
        using var stream = typeof(TranslatorCreditsCatalog).Assembly
            .GetManifestResourceStream("Borea.App.Localization.Translators.json")
            ?? throw new InvalidDataException("The translator credits resource is missing.");
        return Read(stream);
    }
}
