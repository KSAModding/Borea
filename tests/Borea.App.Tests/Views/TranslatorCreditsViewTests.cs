using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Borea.App.Localization;
using Borea.App.Tests.ViewModels;
using Borea.App.Views.Settings;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class TranslatorCreditsViewTests
{
    [Theory]
    [InlineData(860, "Dark")]
    [InlineData(1280, "Light")]
    public async Task About_ShowsNamesLinksAndPlainTextAndRefreshesTheHeading(double width, string theme)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var folder = Directory.CreateDirectory(Path.Combine(harness.Root, "Languages")).FullName;
        File.WriteAllText(Path.Combine(folder, "Resources.fr.resx"),
            """<root><data name="AboutTranslators"><value>Heading from a local translation</value></data><data name="AboutOriginalRepository"><value>Repository link from a local translation</value></data></root>""");
        await HeadlessApp.RunAsync(harness, async () =>
        {
            var originalTheme = Application.Current!.RequestedThemeVariant;
            var originalCulture = CultureInfo.CurrentUICulture;
            var localization = new LocalizationService(CultureInfo.GetCultureInfo("en"), TranslationFiles.Load(folder, _ => { }));
            var viewModel = harness.NewViewModel(localization: localization);
            Application.Current.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var page = new AboutSettings();
            var window = new Window { Width = width, Height = 1000, Content = page, DataContext = viewModel };
            window.Show();
            try
            {
                // A fixture models a newly shipped language without registering it in production.
                var rows = page.FindControl<ItemsControl>("TranslatorCreditsList")!;
                rows.ItemsSource = new[]
                {
                    viewModel.TranslatorCredits.Single(group => group.IsOriginalLanguage),
                    new LanguageTranslatorCredits(new SupportedCulture("de", "Deutsch"),
                        [new TranslatorCredit("First", "https://example.com/first"), new TranslatorCredit("Second"),
                            .. Enumerable.Range(1, 20).Select(number => new TranslatorCredit($"Translator {number}"))]),
                    TranslatorCreditsCatalog.ForLanguages([new SupportedCulture("zh-Hans", "\u7b80\u4f53\u4e2d\u6587")]).Single(),
                };
                window.UpdateLayout();
                page.GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
                await HeadlessApp.FramesAsync(2);
                var controls = page.GetVisualDescendants().ToArray();
                var linked = controls.OfType<Button>().Single(button => Equals(button.Content, "kuxiaowo"));
                Assert.True(linked.IsEffectivelyVisible);
                Assert.Equal("https://github.com/kuxiaowo", linked.CommandParameter);
                Assert.Same(viewModel.OpenAboutLinkCommand, linked.Command);
                var opened = new List<string>();
                viewModel.OpenWithSystem = opened.Add;
                linked.Command!.Execute(linked.CommandParameter);
                var repository = controls.OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Borea repository"));
                Assert.True(repository.IsEffectivelyVisible);
                Assert.Equal("https://github.com/KSAModding/Borea", repository.CommandParameter);
                Assert.Same(viewModel.OpenAboutLinkCommand, repository.Command);
                repository.Command!.Execute(repository.CommandParameter);
                Assert.Equal(["https://github.com/kuxiaowo", "https://github.com/KSAModding/Borea"], opened);
                var plain = controls.OfType<TextBlock>().Single(text => text.Text == "Second");
                Assert.True(plain.IsEffectivelyVisible);
                Assert.DoesNotContain(controls.OfType<Button>().Where(button => button.IsEffectivelyVisible),
                    button => Equals(button.Content, "Second"));
                Assert.Contains(controls.OfType<TextBlock>(), text => text.Text == "Translators");
                Assert.True(linked.Bounds.Width > 0);
                Assert.True(rows.Bounds.Width <= page.Bounds.Width);
                var last = controls.OfType<TextBlock>().Single(text => text.Text == "Translator 20");
                Assert.True(last.TranslatePoint(default, rows)!.Value.Y > plain.TranslatePoint(default, rows)!.Value.Y);

                localization.TrySetCulture("fr");
                await HeadlessApp.FramesAsync(1);
                window.UpdateLayout();
                Assert.Equal("Heading from a local translation", localization.AboutTranslators);
                Assert.Equal("kuxiaowo", linked.Content);
                Assert.Equal("Repository link from a local translation", repository.Content);
                Assert.Equal("https://github.com/KSAModding/Borea", repository.CommandParameter);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Heading from a local translation");
                Assert.DoesNotContain(viewModel.TranslatorCredits, group => group.Language.Name == "fr");

                localization.TrySetCulture("en");
                rows.ItemsSource = TranslatorCreditsCatalog.ForLanguages(
                    [.. LocalizationService.BuiltInCultures, new SupportedCulture("zh-Hans", "\u7b80\u4f53\u4e2d\u6587")]);
                window.UpdateLayout();
                await HeadlessApp.FramesAsync(1);

                var folder = Environment.GetEnvironmentVariable("BOREA_TRANSLATOR_CREDITS_SCREENSHOTS");
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    using var stream = File.Create(Path.Combine(folder, $"translator-credits-{width}-{theme}.png"));
                    frame.Save(stream, PngBitmapEncoderOptions.Default);
                }
            }
            finally
            {
                window.Close();
                harness.Localization.TrySetCulture(originalCulture.Name);
                TranslationResourceManager.Current.Files = TranslationFiles.None;
                Resources.Culture = originalCulture;
                CultureInfo.CurrentUICulture = originalCulture;
                Application.Current.RequestedThemeVariant = originalTheme;
            }

            return true;
        });
    }
}
