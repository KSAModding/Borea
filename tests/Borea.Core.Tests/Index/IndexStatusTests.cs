using System.Globalization;
using Borea.Core.Index;

namespace Borea.Core.Tests.Index;

public sealed class IndexStatusTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void Constructor_CanonicalUtcTimestamp_UsesTheSameInstantInEveryCulture(string cultureName)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            var status = new IndexStatus(
                IndexStatusState.Disputed,
                "disputed",
                "2026-08-08T12:00:00Z",
                "Ownership is disputed.");

            Assert.Equal(new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero), status.Since);
            Assert.Equal("disputed", status.RawState);
            Assert.Equal("Ownership is disputed.", status.Reason);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData("2026-08-08 12:00:00Z")]
    [InlineData("2026-08-08T14:00:00+02:00")]
    [InlineData("08/08/2026 12:00:00")]
    public void Constructor_NonCanonicalOrNonUtcTimestamp_Throws(string since)
    {
        Assert.Throws<FormatException>(() => new IndexStatus(IndexStatusState.Delisted, "delisted", since));
    }
}
