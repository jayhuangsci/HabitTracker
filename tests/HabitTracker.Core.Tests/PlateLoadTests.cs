using HabitTracker.Core;

namespace HabitTracker.Core.Tests;

public class PlateLoadTests
{
    [Fact]
    public void SignatureStackIncludesBothSleevesAndBar()
    {
        var load = PlateLoad.Parse(45, "45lbs+25lbs+10", null, true, WeightUnit.Pounds);
        Assert.Equal(205m, load.TotalWeight);
        Assert.Equal(80m, load.LeftWeight);
        Assert.True(load.IsBalanced);
        Assert.Equal(new decimal[] { 45, 25, 10 }, load.RightPlates);
    }

    [Fact]
    public void IndependentStacksPreserveAnUnevenLoad()
    {
        var load = PlateLoad.Parse(45, "45 + 25", "45 + 10", false, WeightUnit.Pounds);
        Assert.Equal(170, load.TotalWeight);
        Assert.False(load.IsBalanced);
    }

    [Fact]
    public void MetricFractionalPlatesAndEmptyBarWork()
    {
        Assert.Equal(72.5m, PlateLoad.Parse(20, "20kg+5+1.25", null, true, WeightUnit.Kilograms).TotalWeight);
        Assert.Equal(45m, PlateLoad.Parse(45, "", "", true, WeightUnit.Pounds).TotalWeight);
    }

    [Theory]
    [InlineData("45++25")]
    [InlineData("-45")]
    [InlineData("0")]
    [InlineData("2,5")]
    [InlineData("20kg")]
    [InlineData("45+garbage")]
    [InlineData("999999999999999999999999999999999999")]
    public void RejectsAmbiguousOrInvalidInput(string expression) =>
        Assert.Throws<ArgumentException>(() => PlateLoad.Parse(45, expression, null, true, WeightUnit.Pounds));

    [Fact]
    public void StackCannotBeMutatedThroughItsSourceArray()
    {
        decimal[] plates = [45];
        var load = new PlateLoad(45, plates, plates, WeightUnit.Pounds);
        plates[0] = 100;
        Assert.Equal(135, load.TotalWeight);
    }

    [Fact]
    public void RejectsOversizedStacksAndUnknownEnums()
    {
        Assert.Throws<ArgumentException>(() => new PlateLoad(45, Enumerable.Repeat(10m, 17), [], WeightUnit.Pounds));
        Assert.Throws<ArgumentException>(() => new PlateLoad(45, [], [], (WeightUnit)42));
    }

    [Fact]
    public void ConversionRoundTripsWithoutDisplayRounding()
    {
        var kg = PlateLoad.Convert(205, WeightUnit.Pounds, WeightUnit.Kilograms);
        Assert.Equal(92.98643585m, kg);
        Assert.Equal(205, PlateLoad.Convert(kg, WeightUnit.Kilograms, WeightUnit.Pounds));
    }
}
