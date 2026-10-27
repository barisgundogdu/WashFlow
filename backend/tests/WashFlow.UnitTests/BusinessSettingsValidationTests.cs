using WashFlow.Application.Businesses;
namespace WashFlow.UnitTests;

public sealed class BusinessSettingsValidationTests
{
    private static BusinessSettingsInput Input() => new() { Name = "Business", Version = 1, TimeZoneId = "America/New_York", WeeklyHours = Enumerable.Range(1, 7).Select(d => new OpeningDay(d, d == 7, d == 7 ? null : 540, d == 7 ? null : 1020)).ToArray() };
    [Fact] public void ValidScheduleSupportsClosedDaysAndNonTurkishTimezone() => Assert.Empty(BusinessSettingsValidation.Validate(Input()));
    [Fact] public void UnconfiguredProfileNeedsNoInventedHoursOrTimezone() => Assert.Empty(BusinessSettingsValidation.Validate(new() { Name = "Business", Version = 1 }));
    [Theory]
    [InlineData(0, false, 540, 1020)]
    [InlineData(8, false, 540, 1020)]
    [InlineData(1, false, -1, 1020)]
    [InlineData(1, false, 540, 540)]
    [InlineData(1, false, 1020, 540)]
    [InlineData(1, false, 0, 1441)]
    [InlineData(1, false, null, 1020)]
    [InlineData(1, true, 540, null)]
    [InlineData(1, true, null, 1020)]
    public void RejectsInvalidIntervals(int day, bool closed, int? open, int? close)
    {
        var input = Input(); input.WeeklyHours = [new(day, closed, open, close), .. input.WeeklyHours!.Skip(1)];
        Assert.Contains("weeklyHours", BusinessSettingsValidation.Validate(input).Keys);
    }
    [Fact] public void AcceptsWholeDayWithExclusiveMidnightBoundary() { var i = Input(); i.WeeklyHours = [new(1, false, 0, 1440), .. i.WeeklyHours!.Skip(1)]; Assert.Empty(BusinessSettingsValidation.Validate(i)); }
    [Fact] public void RejectsDuplicateMissingAndNullDays() { foreach (var days in new IReadOnlyList<OpeningDay>[] { [], [new(1, true, null, null), new(1, true, null, null)], [null!, .. Input().WeeklyHours!.Skip(1)] }) { var i = Input(); i.WeeklyHours = days; Assert.Contains("weeklyHours", BusinessSettingsValidation.Validate(i).Keys); } }
    [Theory]
    [InlineData("Turkey Standard Time")]
    [InlineData("Europe/Unknown")]
    [InlineData("")]
    public void RejectsInvalidTimezoneWithConfiguredHours(string id) { var i = Input(); i.TimeZoneId = id; Assert.Contains("timeZoneId", BusinessSettingsValidation.Validate(i).Keys); }
    [Theory]
    [InlineData("Name")]
    [InlineData("Phone")]
    [InlineData("ContactEmail")]
    [InlineData("AddressLine1")]
    [InlineData("Description")]
    [InlineData("CountryCode")]
    [InlineData("Version")]
    public void RejectsInvalidFields(string field) { var i = Input(); switch (field) { case "Name": i.Name = " "; break; case "Phone": i.Phone = "not a phone"; break; case "ContactEmail": i.ContactEmail = "invalid"; break; case "AddressLine1": i.AddressLine1 = new('a', 251); break; case "Description": i.Description = new('a', 2001); break; case "CountryCode": i.CountryCode = "123"; break; case "Version": i.Version = null; break; } Assert.NotEmpty(BusinessSettingsValidation.Validate(i)); }
    [Theory]
    [InlineData("\0")]
    [InlineData("\n")]
    [InlineData("\t")]
    public void RejectsControlCharactersInProfileFields(string control) { var i = Input(); i.AddressLine1 = "Address" + control; Assert.Contains("addressLine1", BusinessSettingsValidation.Validate(i).Keys); }
    [Fact] public void RejectsDisplayNameEmailSyntax() { var i = Input(); i.ContactEmail = "Person <test@example.test>"; Assert.Contains("contactEmail", BusinessSettingsValidation.Validate(i).Keys); }
}
