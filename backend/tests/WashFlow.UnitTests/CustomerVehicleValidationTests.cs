using System.Globalization;
using WashFlow.Application.Customers;
namespace WashFlow.UnitTests;

public sealed class CustomerVehicleValidationTests
{
    [Theory]
    [InlineData("34abc123", "34 ABC 123")]
    [InlineData(" 06  ab  1234 ", "06 AB 1234")]
    [InlineData("81Z99", "81 Z 99")]
    [InlineData("D-MÜ 123", "D-MÜ 123")]
    [InlineData("ab-123-cd", "ab-123-cd")]
    [InlineData("東京 123", "東京 123")]
    public void PlatesPreserveForeignDisplayAndCanonicalizeRecognizedTurkishFormat(string input, string expected) => Assert.Equal(expected, CustomerVehicleValidation.PlateDisplay(input));
    [Fact] public void PlateSearchIsCultureIndependentAndIgnoresSpacing() { var old = CultureInfo.CurrentCulture; try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR"); Assert.Equal("34ABC123", CustomerVehicleValidation.SearchKey("34 abc 123")); Assert.Equal("IST123", CustomerVehicleValidation.SearchKey("ist123")); Assert.Equal(CustomerVehicleValidation.NameSearchKey("İlker"), CustomerVehicleValidation.NameSearchKey("ilker")); } finally { CultureInfo.CurrentCulture = old; } }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\0")]
    public void CustomerRequiresSafeName(string value) => Assert.Contains("fullName", CustomerVehicleValidation.Customer(new() { FullName = value }, false).Keys);
    [Fact] public void CustomerOptionalFieldsAndVersionsAreValidated() { Assert.Empty(CustomerVehicleValidation.Customer(new() { FullName = "Ayşe" }, false)); Assert.NotEmpty(CustomerVehicleValidation.Customer(new() { FullName = "Ayşe", Email = "invalid", Phone = "hello", Notes = new('x', 2001) }, false)); Assert.Contains("version", CustomerVehicleValidation.Customer(new() { FullName = "Ayşe" }, true).Keys); }
    private static VehicleInput Valid() => new() { Plate = "34 ABC 123", Brand = "Test brand", Model = "Test model", Category = "Car" };
    [Theory]
    [InlineData("Plate")]
    [InlineData("Brand")]
    [InlineData("Model")]
    [InlineData("Category")]
    [InlineData("Year")]
    [InlineData("Notes")]
    [InlineData("Color")]
    [InlineData("Version")]
    public void RejectsInvalidVehicle(string field) { var i = Valid(); switch (field) { case "Plate": i.Plate = "---"; break; case "Brand": i.Brand = ""; break; case "Model": i.Model = new('x', 101); break; case "Category": i.Category = "Owner"; break; case "Year": i.ModelYear = 2101; break; case "Notes": i.Notes = new('x', 2001); break; case "Color": i.Color = new('x', 61); break; } Assert.NotEmpty(CustomerVehicleValidation.Vehicle(i, field == "Version")); }
    [Theory][InlineData(1886)][InlineData(2100)] public void YearBoundariesAndAllCategoriesSupported(int year) { foreach (var category in CustomerVehicleValidation.Categories) { var i = Valid(); i.ModelYear = year; i.Category = category; Assert.Empty(CustomerVehicleValidation.Vehicle(i, false)); } }
    [Theory][InlineData(0, 20)][InlineData(10001, 20)][InlineData(1, 0)][InlineData(1, 51)] public void RejectsUnboundedPagination(int page, int size) => Assert.False(CustomerVehicleValidation.ValidPage(null, page, size));
    [Fact] public void SearchBoundedAndPhoneKeyIgnoresPunctuation() { Assert.False(CustomerVehicleValidation.ValidPage(new('x', 101), 1, 20)); Assert.Equal("905551234567", CustomerVehicleValidation.PhoneKey("+90 (555) 123-45-67")); }
}
