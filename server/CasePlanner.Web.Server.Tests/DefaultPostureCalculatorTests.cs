using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

public sealed class DefaultPostureCalculatorTests
{
    [Fact]
    public void IsLikelyDefault_AnswerFiled_NeverWarnsRegardlessOfDates()
    {
        Assert.False(DefaultPostureCalculator.IsLikelyDefault(true, "2020-01-01", new DateOnly(2026, 1, 1)));
        Assert.False(DefaultPostureCalculator.IsLikelyDefault(true, null, new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void IsLikelyDefault_NoAnswer_UnderThreshold_DoesNotWarn()
    {
        var servicePerfected = "2026-01-01";
        var asOf = new DateOnly(2026, 1, 1).AddDays(DefaultPostureCalculator.NoAnswerThresholdDays - 1);
        Assert.False(DefaultPostureCalculator.IsLikelyDefault(false, servicePerfected, asOf));
    }

    [Fact]
    public void IsLikelyDefault_NoAnswer_ExactlyAtThreshold_Warns()
    {
        var servicePerfected = "2026-01-01";
        var asOf = new DateOnly(2026, 1, 1).AddDays(DefaultPostureCalculator.NoAnswerThresholdDays);
        Assert.True(DefaultPostureCalculator.IsLikelyDefault(false, servicePerfected, asOf));
    }

    [Fact]
    public void IsLikelyDefault_NoAnswer_OverThreshold_Warns()
    {
        var servicePerfected = "2026-01-01";
        var asOf = new DateOnly(2026, 1, 1).AddDays(DefaultPostureCalculator.NoAnswerThresholdDays + 30);
        Assert.True(DefaultPostureCalculator.IsLikelyDefault(false, servicePerfected, asOf));
    }

    [Fact]
    public void IsLikelyDefault_NoServicePerfectedDate_NeverWarns()
    {
        Assert.False(DefaultPostureCalculator.IsLikelyDefault(false, null, new DateOnly(2030, 1, 1)));
        Assert.False(DefaultPostureCalculator.IsLikelyDefault(false, "", new DateOnly(2030, 1, 1)));
        Assert.False(DefaultPostureCalculator.IsLikelyDefault(false, "not-a-date", new DateOnly(2030, 1, 1)));
    }
}
