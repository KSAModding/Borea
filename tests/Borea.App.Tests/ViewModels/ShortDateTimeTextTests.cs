using System.Globalization;
using Borea.App.ViewModels;

namespace Borea.App.Tests.ViewModels;

/// <summary>The short date follows the culture, so each case sets the one it expects.</summary>
public sealed class ShortDateTimeTextTests : IDisposable
{
    /// <summary>An evening, and a morning of the next day as the time to compare it with.</summary>
    private static readonly DateTimeOffset At = new(new DateTime(2026, 9, 19, 19, 27, 0, DateTimeKind.Local));

    private static readonly DateTimeOffset Now = new(new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Local));

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    [InlineData("hu-HU")]
    [InlineData("ja-JP")]
    public void ThisYear_DropsTheYearAndKeepsTheTime(string cultureName)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

        var text = MainViewModel.ShortDateTimeText(At, Now);

        Assert.DoesNotContain(At.Year.ToString(CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        Assert.EndsWith(At.ToString("t", CultureInfo.CurrentCulture), text, StringComparison.Ordinal);
        Assert.True(text.Length < MainViewModel.DateTimeText(At).Length);
    }

    [Fact]
    public void ThisYear_German_ShowsTheDayAndTheMonthWithTheTime()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        Assert.Equal("19.09. 19:27", MainViewModel.ShortDateTimeText(At, Now));
    }

    [Fact]
    public void AnotherYear_KeepsTheFullDate()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        var at = At.AddYears(-1);

        Assert.Equal(MainViewModel.DateTimeText(at), MainViewModel.ShortDateTimeText(at, Now));
    }

    /// <summary>A date pattern that keeps an era or a quoted year marker is no longer a date without its year.</summary>
    [Theory]
    [InlineData("ar-SA")]
    [InlineData("bg-BG")]
    [InlineData("ps-AF")]
    public void EraOrQuotedYearMarker_KeepsTheFullDate(string cultureName)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

        Assert.Equal(MainViewModel.DateTimeText(At), MainViewModel.ShortDateTimeText(At, Now));
    }

    /// <summary>February and September are one Persian year apart although they are the same Gregorian one.</summary>
    [Fact]
    public void AnotherYearOfThePersianCalendar_KeepsTheFullDate()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");
        var at = new DateTimeOffset(new DateTime(2026, 2, 1, 19, 27, 0, DateTimeKind.Local));

        Assert.Equal(MainViewModel.DateTimeText(at), MainViewModel.ShortDateTimeText(at, Now));
    }
}
