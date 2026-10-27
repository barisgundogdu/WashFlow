using System.Text.Json;
using WashFlow.Application.Scheduling;

namespace WashFlow.UnitTests;

public sealed class SchedulingApiContractTests
{
    [Theory]
    [InlineData(0, 1, 20, false)]
    [InlineData(31, 1, 50, true)]
    [InlineData(32, 1, 20, false)]
    [InlineData(1, 0, 20, false)]
    [InlineData(1, 10001, 20, false)]
    [InlineData(1, 1, 51, false)]
    [InlineData(1, 10000, 1, true)]
    public void DatedAvailabilityQueriesHaveExplicitBounds(int days, int page, int size, bool valid)
    {
        var from = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(valid, SchedulingValidation.AvailabilityRange(new(from, from.AddDays(days), page, size)).Count == 0);
    }
    [Fact]
    public void RequirementConfirmationMustBeExplicitEvenWhenFalse()
    {
        var json = $$"""{"workAreaId":"{{Guid.NewGuid()}}"} """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RequirementInput>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var input = new RequirementInput(Guid.NewGuid(), false, null);
        Assert.Equal(input, JsonSerializer.Deserialize<RequirementInput>(JsonSerializer.Serialize(input)));
    }
}
