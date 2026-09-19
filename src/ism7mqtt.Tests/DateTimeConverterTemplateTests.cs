using System;
using ism7mqtt.ISM7.Xml;
using Xunit;

namespace ism7mqtt.Tests;

public class DateTimeConverterTemplateTests
{
    private static DateTimeConverterTemplate CreateConverter(string type, params ushort[] telegramNumbers)
    {
        return new DateTimeConverterTemplate
        {
            CTID = 1,
            Type = type,
            TelegramNumbers = new List<ushort>(telegramNumbers),
        };
    }

    [Fact]
    public void Time_HasValue_OnlyAfterAllTelegramsReceived_OutOfOrder()
    {
        var converter = CreateConverter("TIME_hh_mm_ss", 293, 294, 295);

        converter.AddTelegram(294, low: 34, high: 0); // minute
        Assert.False(converter.HasValue);

        converter.AddTelegram(295, low: 56, high: 0); // second
        Assert.False(converter.HasValue);

        converter.AddTelegram(293, low: 12, high: 0); // hour
        Assert.True(converter.HasValue);

        var value = converter.GetValue();
        Assert.Equal(new TimeSpan(12, 34, 56).ToString(), value.ToString());

        Assert.False(converter.HasValue);
    }

    [Fact]
    public void Date_HasValue_OnlyAfterAllTelegramsReceived()
    {
        var converter = CreateConverter("Date_DD_MM_YY", 290, 291, 292);

        converter.AddTelegram(290, low: 17, high: 0); // day
        converter.AddTelegram(291, low: 3, high: 0);  // month
        Assert.False(converter.HasValue);

        converter.AddTelegram(292, low: 24, high: 0); // year
        Assert.True(converter.HasValue);

        var value = converter.GetValue();
        Assert.Equal(new DateTime(2024, 3, 17).ToString(), value.ToString());

        Assert.False(converter.HasValue);
    }

    [Fact]
    public void InvalidHour_IsIgnored_AndDoesNotCompleteTheValue()
    {
        var converter = CreateConverter("TIME_hh_mm_ss", 293, 294, 295);

        converter.AddTelegram(293, low: 99, high: 0); // invalid hour
        converter.AddTelegram(294, low: 34, high: 0);
        converter.AddTelegram(295, low: 56, high: 0);
        Assert.False(converter.HasValue);

        converter.AddTelegram(293, low: 12, high: 0); // valid hour arrives later
        Assert.True(converter.HasValue);

        var value = converter.GetValue();
        Assert.Equal(new TimeSpan(12, 34, 56).ToString(), value.ToString());
    }

    [Fact]
    public void UnknownType_GetValue_DoesNotThrow_AndReturnsNull()
    {
        var converter = CreateConverter("SOMETHING_ELSE", 293, 294, 295);

        converter.AddTelegram(293, low: 12, high: 0);
        converter.AddTelegram(294, low: 34, high: 0);
        converter.AddTelegram(295, low: 56, high: 0);
        Assert.True(converter.HasValue);

        var value = converter.GetValue();

        Assert.Null(value);
    }

    [Fact]
    public void AddTelegram_UnrelatedTelegramNumber_IsIgnored()
    {
        var converter = CreateConverter("TIME_hh_mm_ss", 293, 294, 295);

        converter.AddTelegram(999, low: 1, high: 0);

        Assert.False(converter.HasValue);
    }

    [Fact]
    public void AddTelegram_MissingTelegramNumbers_DoesNotThrow()
    {
        var converter = new DateTimeConverterTemplate
        {
            CTID = 1,
            Type = "TIME_hh_mm_ss",
            TelegramNumbers = null,
        };

        converter.AddTelegram(293, low: 12, high: 0);

        Assert.False(converter.HasValue);
    }
}
