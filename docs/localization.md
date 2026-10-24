# Help translate Borea

You do not need to write C# to translate Borea. The easiest way is [Hosted Weblate](https://hosted.weblate.org/engage/borea/) in your browser. You can also edit a resource file through the GitHub website and submit the change for review.

## Translate on Weblate

1. Open the [Borea project on Hosted Weblate](https://hosted.weblate.org/projects/borea/borea-app/) and sign in.
2. Select your language, or start a new one. Weblate shows how much of each language is translated.
3. Translate the texts that you can. A text that you do not translate shows in English in Borea.

Weblate sends the changes to Borea as a pull request, which a maintainer reviews like every other change, so they reach Borea with the next release. Weblate checks the placeholders, see [Preserve placeholders](#preserve-placeholders). A new language also needs the small code change of [Propose a new language](#propose-a-new-language), which a maintainer makes.

## Find the language files

Borea keeps user-interface text in [`src/Borea.App/Localization/Resources`](../src/Borea.App/Localization/Resources).

- [`Resources.resx`](../src/Borea.App/Localization/Resources/Resources.resx) is the neutral English source.
- [`Resources.de.resx`](../src/Borea.App/Localization/Resources/Resources.de.resx) is the German translation.
- [`Resources.zh-Hans.resx`](../src/Borea.App/Localization/Resources/Resources.zh-Hans.resx) is the Simplified Chinese translation, listed as `简体中文`.
- [`Resources.en-QP.resx`](../src/Borea.App/Localization/Resources/Resources.en-QP.resx) is pirate speak. `QP` is a private-use region code, because .NET has no culture for pirate speak.
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

The translated value must still contain `{0}`. A test fails when a translation does not have the same placeholders as the English text.

## Propose a new language

Adding a new language needs a resource file and one small code change that registers the language in the selector. A translator can provide the resource file, and a maintainer can make the code change. A complete file is best, but Borea shows a text in English while its translation is missing, so you can start with the text that matters most.

1. Open an issue and state the language, its culture name, and whether you can translate it.
2. Copy the neutral [`Resources.resx`](../src/Borea.App/Localization/Resources/Resources.resx) file to `Resources.<language>.resx`.
3. Translate the `<value>` elements that you can, and keep every `name` attribute unchanged.
4. Delete the `data` elements that you do not translate, because Borea then shows their English text and keeps that text correct when the English text changes.
5. Keep product names, identifiers, paths, and placeholders unchanged unless their surrounding text needs a different word order.
6. Submit one pull request for the new language.
7. Ask a fluent speaker to review the translation when possible.

You can try the new file in Borea before a maintainer registers the language, see [Preview a translation in Borea](#preview-a-translation-in-borea).

A maintainer will add the language to [`LocalizationService`](../src/Borea.App/Localization/LocalizationService.cs) and to `SatelliteResourceLanguages` in [`Borea.App.csproj`](../src/Borea.App/Borea.App.csproj). A culture name that .NET does not know, such as `en-QP`, also needs `Culture` and `LogicalName` metadata on its file in that project. The resource parity tests check that the new file has no key that the English source does not have, and that its texts have the placeholders of the English ones. A text that the file does not have shows in English.

## Preview a translation in Borea

You can see a translation in Borea without git, the .NET SDK, or a build. Borea reads translation files from a folder when it starts, and a text in such a file shows instead of the text that Borea ships.

1. In Borea, open Settings, About, and select Open Borea folder.
2. In that folder, create a folder named `Languages` if it does not exist.
3. Put the translation file into `Languages`. Keep the name `Resources.<language>.resx`, for example `Resources.fr.resx`.
4. Close Borea and start it again, because Borea reads the folder only when it starts.
5. In Settings, General, select the language. A language that Borea does not ship yet, such as French, shows in the list when its file is in the folder.
6. Turn on Mark untranslated texts to find the texts that the file does not translate yet. Borea shows each of them in English between two stars, for example `** Home **`.

Settings, About names the translation files that Borea uses, and so does the text that Copy diagnostics puts on the clipboard for a bug report.

Borea does not use a text whose placeholders differ from the English text, or whose key the English source does not have. It shows that text in English, or in the translation that Borea ships, and writes one line about it to the Borea log (Settings, About, Open Borea log). Borea does not use a file that is not valid XML, or a file that has an entry with a `type` or `mimetype` attribute, because a translation file holds only text.

To see the texts that Borea ships again, remove the file from `Languages` and start Borea again. When the language of the removed file was selected, Borea shows the language of the system, or English when Borea does not ship that language.

## Translation guidance

- Write clear and natural text for the target language instead of translating each English word separately.
- Use the same translation for the same term throughout the interface.
- Keep button and navigation labels short.
- Do not translate `Borea`, file paths, resource keys, or code identifiers.
- Do not add explanations that are not present in the English source. Open an issue when the source text is unclear.

## Optional local check

Continuous integration checks every pull request. A text-only contributor can wait for that result.

If you already have the .NET SDK, you can run the focused tests locally:

On Linux, the Simplified Chinese rendering checks also need a system font documented in the [Linux installation instructions](../README.md#linux), for example `fonts-noto-cjk` or `fonts-wqy-microhei` on Debian and Ubuntu. CI uses the smaller `fonts-wqy-microhei` package. The checks keep asserting that every Chinese glyph renders; a minimal system without a Chinese font will fail them.

```text
dotnet test tests/Borea.App.Tests/Borea.App.Tests.csproj --no-restore
```

The parity tests report a failure when a translation file has a key that the English source does not have, or when a translation does not have the placeholders of the English text. A key that a translation file misses is not a failure, because Borea then shows the English text.

## Technical references

- [Localizing using ResX in Avalonia](https://docs.avaloniaui.net/docs/app-development/localizing)
- [Resources in .NET apps](https://learn.microsoft.com/en-us/dotnet/core/extensions/resources)
- [Localization in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/localization)
