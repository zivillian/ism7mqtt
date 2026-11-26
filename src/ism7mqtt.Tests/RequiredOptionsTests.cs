using ism7mqtt;
using Xunit;

namespace ism7mqtt.Tests;

public class RequiredOptionsTests
{
    [Fact]
    public void NothingMissing_WhenAllThreeAreSet()
    {
        Assert.Empty(RequiredOptions.Missing("192.168.0.215", "broker", "secret"));
    }

    [Fact]
    public void EmptyMqttHost_IsNamedWithOptionAndEnvironmentVariable()
    {
        var missing = RequiredOptions.Missing("192.168.0.215", "", "secret");

        var entry = Assert.Single(missing);
        Assert.Contains("--mqttServer", entry);
        Assert.Contains("ISM7_MQTTHOST", entry);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("  \r\n")]
    public void WhitespaceOnly_CountsAsMissing(string value)
    {
        var missing = RequiredOptions.Missing(value, value, value);

        Assert.Equal(3, missing.Count);
    }

    [Fact]
    public void AllMissing_NamesAllThreeInOrder()
    {
        var missing = RequiredOptions.Missing(null, null, null);

        Assert.Collection(missing,
            e => Assert.Contains("ISM7_IP", e),
            e => Assert.Contains("ISM7_MQTTHOST", e),
            e => Assert.Contains("ISM7_PASSWORD", e));
    }
}
