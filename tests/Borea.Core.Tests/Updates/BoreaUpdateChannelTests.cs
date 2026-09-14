using Borea.Core.Mods;
using Borea.Core.Updates;

namespace Borea.Core.Tests.Updates;

public sealed class BoreaUpdateChannelTests
{
    [Theory]
    [InlineData(BoreaUpdateChannel.Stable, "1.0.0", true)]
    [InlineData(BoreaUpdateChannel.Stable, "1.0.0-beta.1", false)]
    [InlineData(BoreaUpdateChannel.Testing, "1.0.0", true)]
    [InlineData(BoreaUpdateChannel.Testing, "1.0.0-beta.1", true)]
    [InlineData(BoreaUpdateChannel.Testing, "1.0.0-Beta", true)]
    [InlineData(BoreaUpdateChannel.Testing, "1.0.0-beta2", false)]
    [InlineData(BoreaUpdateChannel.Testing, "1.0.0-dev.1", false)]
    [InlineData(BoreaUpdateChannel.Testing, "1.0.0-rc.1", false)]
    [InlineData(BoreaUpdateChannel.Dev, "1.0.0", true)]
    [InlineData(BoreaUpdateChannel.Dev, "1.0.0-dev.1", true)]
    [InlineData(BoreaUpdateChannel.Dev, "1.0.0-rc.1", true)]
    public void Includes_FollowsTheTagName(BoreaUpdateChannel channel, string version, bool expected)
        => Assert.Equal(expected, channel.Includes(ModVersion.Parse(version)));
}
