# Help translate Borea

You do not need to write C# to improve an existing Borea translation. You can edit a resource file through the GitHub website and submit the change for review.

## Find the language files

Borea keeps user-interface text in [`src/Borea.App/Localization/Resources`](../src/Borea.App/Localization/Resources).

- [`Resources.resx`](../src/Borea.App/Localization/Resources/Resources.resx) is the neutral English source.
- [`Resources.de.resx`](../src/Borea.App/Localization/Resources/Resources.de.resx) is the German translation.
- A translated file uses the name `Resources.<language>.resx`. For example, French uses `Resources.fr.resx`.

The language part of the file name is a .NET culture name. Microsoft documents these names through [`CultureInfo.Name`](https://learn.microsoft.com/en-us/dotnet/api/system.globalization.cultureinfo.name).

## Improve an existing translation on GitHub

1. Open the resource file for the language that you want to improve.
2. Select the pencil icon to edit the file.
3. Find the text inside the `<value>` element.
4. Change only the user-visible text. Do not change the value of the `name` attribute.
5. Check the proposed diff and select the option to create a pull request.
6. In the pull request, state which language you changed and briefly explain the correction.

For example, this entry uses `NavigationHome` as its stable key and `Home` as its English text:

```xml
<data name="NavigationHome" xml:space="preserve">
  <value>Home</value>
</data>
```

To improve a translation, edit the text between `<value>` and `</value>`. Keep the `NavigationHome` key and the XML tags unchanged.

GitHub creates a personal fork automatically when you do not have direct write access. You do not need to install Borea or a development environment for a text-only correction.

## Preserve placeholders

Some text can contain placeholders such as `{0}`. A placeholder is replaced with a value while Borea runs.

Keep every placeholder in the translation. You can move it to a natural position in the sentence, but do not translate it or change its number.

For example:

```xml
<data name="ViewNotFoundFormat" xml:space="preserve">
  <value>View not found: {0}</value>
</data>
```

The translated value must still contain `{0}`.

## Propose a new language

Adding a new language needs a complete resource file and one small code change that registers the language in the selector. A translator can provide the resource file, and a maintainer can make the code change.

1. Open an issue and state the language, its culture name, and whether you can translate it.
2. Copy the neutral [`Resources.resx`](../src/Borea.App/Localization/Resources/Resources.resx) file to `Resources.<language>.resx`.
3. Translate every `<value>` element and keep every `name` attribute unchanged.
4. Keep product names, identifiers, paths, and placeholders unchanged unless their surrounding text needs a different word order.
5. Submit one pull request for the new language.
6. Ask a fluent speaker to review the translation when possible.

A maintainer will add the language to [`LocalizationService`](../src/Borea.App/Localization/LocalizationService.cs). The resource parity test checks that the new file has the same keys as the English source.

## Translation guidance

- Write clear and natural text for the target language instead of translating each English word separately.
- Use the same translation for the same term throughout the interface.
- Keep button and navigation labels short.
- Do not translate `Borea`, file paths, resource keys, or code identifiers.
- Do not add explanations that are not present in the English source. Open an issue when the source text is unclear.

## Optional local check

Continuous integration checks every pull request. A text-only contributor can wait for that result.

If you already have the .NET SDK, you can run the focused tests locally:

```text
dotnet test tests/Borea.App.Tests/Borea.App.Tests.csproj --no-restore
```

The parity test reports a failure when a translation file has a missing or additional resource key.

## Technical references

- [Localizing using ResX in Avalonia](https://docs.avaloniaui.net/docs/app-development/localizing)
- [Resources in .NET apps](https://learn.microsoft.com/en-us/dotnet/core/extensions/resources)
- [Localization in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/localization)
