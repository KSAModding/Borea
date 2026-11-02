using Borea.Core.Licenses;
using Borea.Storage.Listings;

namespace Borea.Storage.Tests.Listings;

public sealed class LicenseGroupTableTests
{
    [Fact]
    public void EveryLicenseOfTheGroupTable_IsALicenseOfTheSpdxList()
    {
        var list = SpdxLicenseList.Embedded;

        Assert.DoesNotContain(LicenseFilter.Groups.Keys, id => !list.IsLicense(id));
    }
}
