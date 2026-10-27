using WashFlow.Application.Catalog;
namespace WashFlow.UnitTests;
public sealed class CatalogValidationTests
{
    private static ServiceInput Input() => new() { Name = "Dış yıkama", Enabled = true, Published = true, Offerings = [new() { Category = "Car", Currency = "TRY", Price = "450.00", ActiveMinutes = 20, Employees = 1 }] };
    [Theory]
    [InlineData("0", true)] [InlineData("0.00", true)] [InlineData("9999999.99", true)] [InlineData("10000000", false)]
    [InlineData("1.001", false)] [InlineData("-1", false)] [InlineData("1,00", false)] [InlineData("1e2", false)] [InlineData("01.00", false)] [InlineData(" 1", false)] [InlineData(null, false)]
    public void MoneyIsExactAndBounded(string? value, bool valid) => Assert.Equal(valid, CatalogValidation.ValidMoney(value));
    [Fact] public void CanonicalMoneyDoesNotUseCurrentCulture() => Assert.Equal("450.10", CatalogValidation.Money(450.10m));
    [Theory] [InlineData("Car")] [InlineData("Suv")] [InlineData("Van")] [InlineData("Pickup")] [InlineData("Motorcycle")] [InlineData("Other")]
    public void ReusesAllStableCategories(string category) { var i = Input(); i.Offerings![0].Category = category; Assert.Empty(CatalogValidation.Validate(i,false)); }
    [Theory] [InlineData(0,1,null,false)] [InlineData(10081,1,null,false)] [InlineData(1,0,null,false)] [InlineData(1,101,null,false)] [InlineData(1,1,-1,false)] [InlineData(1,1,43201,false)] [InlineData(10080,100,43200,true)] [InlineData(1,1,0,true)]
    public void EstimateBounds(int active,int employees,int? waiting,bool valid) { var i=Input(); var o=i.Offerings![0]; o.ActiveMinutes=active; o.Employees=employees; o.WaitingMinutes=waiting; Assert.Equal(valid,CatalogValidation.Validate(i,false).Count==0); }
    [Fact] public void PublicationAndDraftRules() { var i=Input(); i.Enabled=false; Assert.NotEmpty(CatalogValidation.Validate(i,false)); i.Published=false; i.Offerings=[]; Assert.Empty(CatalogValidation.Validate(i,false)); i.Enabled=true; Assert.NotEmpty(CatalogValidation.Validate(i,false)); }
    [Fact] public void DuplicateUnknownAndMissingOfferingsRejected() { var i=Input(); i.Offerings=[i.Offerings![0],i.Offerings[0]]; Assert.NotEmpty(CatalogValidation.Validate(i,false)); i.Offerings=[new() {Category="Truck"}]; Assert.NotEmpty(CatalogValidation.Validate(i,false)); i.Offerings=null; Assert.NotEmpty(CatalogValidation.Validate(i,false)); }
    [Fact] public void UpdatesRequireVersionAndSafeText() { var i=Input(); Assert.NotEmpty(CatalogValidation.Validate(i,true)); i.Version=1; Assert.Empty(CatalogValidation.Validate(i,true)); i.Name="\0"; Assert.NotEmpty(CatalogValidation.Validate(i,true)); i.Name=new string('a',201); Assert.NotEmpty(CatalogValidation.Validate(i,true)); }
}
