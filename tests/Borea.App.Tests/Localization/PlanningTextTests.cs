using System.Globalization;
using Borea.App.Localization;
using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.App.Tests.Localization;

public sealed class PlanningTextTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    private static readonly ModDependency Library = new("library", ModDependencyKind.Required, ModVersion.Parse("1.0.0"));
    private static readonly ModDependency Alternatives = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("first"), new ModDependencyAlternative("second", ModVersion.Parse("2.0.0"))]);

    [Fact]
    public void EveryMessageKind_HasEnglishAndGermanText()
    {
        foreach (var kind in Enum.GetValues<PlanningMessageKind>())
        {
            var message = Sample(kind);
            var english = Format("en", message);
            var german = Format("de", message);

            Assert.False(string.IsNullOrWhiteSpace(english), kind.ToString());
            Assert.False(string.IsNullOrWhiteSpace(german), kind.ToString());
            Assert.DoesNotContain("{", english);
            Assert.DoesNotContain("{", german);
            Assert.NotEqual(english, german);
            Assert.NotEqual(message.Message, english);
        }
    }

    [Fact]
    public void Dependencies_NameTheirBoundsAndAlternatives()
    {
        var message = new PlanningMessage("flight-tools", PlanningMessageKind.ForeignVersionUnknown) { Dependency = Alternatives };

        Assert.Equal("This mod needs first or second >= 2.0.0. Borea cannot read the installed version.", Format("en", message));
        Assert.Equal("Diese Mod braucht first oder second >= 2.0.0. Borea kann die installierte Version nicht lesen.", Format("de", message));
        Assert.Equal("library >= 1.0.0", PlanningText.Dependency(Library));
    }

    [Fact]
    public void ReleaseChannel_UsesTheStatusOfTheDisplayLanguage()
    {
        var message = new PlanningMessage("flight-tools", PlanningMessageKind.ReleaseChannel) { Version = ModVersion.Parse("2.0.0-dev.1"), Status = ReleaseStatus.Dev, Channel = ReleaseChannel.Stable };
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("de"));

        Assert.Equal($"Version 2.0.0-dev.1 hat den Status {localization.ReleaseDev}, den dein Release-Kanal nicht anbietet.", PlanningText.Message(message));
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Resources.Culture = _originalUiCulture;
    }

    private static string Format(string culture, PlanningMessage message)
    {
        _ = new LocalizationService(CultureInfo.GetCultureInfo(culture));
        return PlanningText.Message(message);
    }

    private static PlanningMessage Sample(PlanningMessageKind kind)
    {
        var alternative = kind is PlanningMessageKind.AlternativeChoice or PlanningMessageKind.InvalidAlternative or PlanningMessageKind.ForeignAlternativeUnknown or PlanningMessageKind.UnsatisfiedAlternative or PlanningMessageKind.RetainedAlternative;
        return new PlanningMessage("flight-tools", kind)
        {
            Dependency = alternative ? Alternatives : Library,
            Version = ModVersion.Parse("2.0.0-dev.1"),
            OtherVersion = ModVersion.Parse("2.1.0"),
            Status = ReleaseStatus.Dev,
            Channel = ReleaseChannel.Stable,
            Compatibility = GameCompatibility.Untested,
            Platform = OsPlatform.Linux,
            Value = kind switch
            {
                PlanningMessageKind.Incompatible => "2026.9.4.5400",
                PlanningMessageKind.PlatformUnknown => "amiga",
                _ when alternative => "second",
                _ => "Broken build.",
            },
            Limit = 100_000,
        };
    }
}
