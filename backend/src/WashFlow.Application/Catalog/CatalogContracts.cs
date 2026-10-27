using System.Globalization;
using System.Text.RegularExpressions;
using WashFlow.Application.Customers;
namespace WashFlow.Application.Catalog;

public sealed class OfferingInput
{
    public string? Category { get; set; }
    public string? Price { get; set; }
    public string? Currency { get; set; }
    public int ActiveMinutes { get; set; }
    public int? WaitingMinutes { get; set; }
    public int Employees { get; set; }
}
public class ServiceInput
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public bool Published { get; set; }
    public IReadOnlyList<OfferingInput>? Offerings { get; set; }
    public uint? Version { get; set; }
}
public sealed class ServiceView : ServiceInput
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public interface ICatalogStore
{
    Task<ManagementResult> List(Guid? actor, Guid business, string? q, string status, int page, int size, CancellationToken ct);
    Task<ManagementResult> Read(Guid actor, Guid business, Guid id, CancellationToken ct);
    Task<ManagementResult> Save(Guid actor, Guid business, Guid? id, ServiceInput input, CancellationToken ct);
    Task<ManagementResult> Archive(Guid actor, Guid business, Guid id, uint version, CancellationToken ct);
}
public static partial class CatalogValidation
{
    public const decimal MaximumPrice = 9999999.99m;
    [GeneratedRegex(@"^(0|[1-9][0-9]{0,6})(\.[0-9]{1,2})?$")]
    private static partial Regex MoneyPattern();
    public static bool ValidMoney(string? value) => value != null && MoneyPattern().IsMatch(value) && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var money) && money <= MaximumPrice;
    public static decimal Money(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    public static Dictionary<string, string[]> Validate(ServiceInput input, bool update)
    {
        var errors = new Dictionary<string, string[]>();
        void Error(string key, string message) => errors[key] = [message];
        bool Invalid(string? s, int max, bool multiline = false) => s?.Length > max || s?.Any(c => char.IsSurrogate(c) || char.IsControl(c) && !(multiline && c is '\n' or '\r' or '\t')) == true;
        if (string.IsNullOrWhiteSpace(input.Name) || Invalid(input.Name, 200)) Error("name", "Hizmet adı 1–200 karakter olmalıdır.");
        if (Invalid(input.Description, 2000, true)) Error("description", "Açıklama en fazla 2000 karakter olmalıdır.");
        if (update && input.Version is null or 0) Error("version", "Güncel kayıt sürümü gereklidir.");
        if (input.Published && !input.Enabled) Error("published", "Yayınlamak için hizmet etkin olmalıdır.");
        var rows = input.Offerings;
        if (rows == null || rows.Count > 6 || (input.Enabled && rows.Count == 0) || rows.Any(x => x == null)) { Error("offerings", "Etkin hizmet için en az bir araç türü belirtin (en fazla altı)."); return errors; }
        if (rows.Select(x => x.Category).Distinct().Count() != rows.Count) Error("offerings", "Araç türleri tekrarlanamaz.");
        for (var n = 0; n < rows.Count; n++)
        {
            var row = rows[n];
            if (!CustomerVehicleValidation.Categories.Contains(row.Category)) Error($"offerings[{n}].category", "Geçerli araç türü seçin.");
            if (!ValidMoney(row.Price)) Error($"offerings[{n}].price", "Fiyat 0–9.999.999,99 TL; en fazla iki ondalık basamak olmalıdır.");
            if (row.Currency != "TRY") Error($"offerings[{n}].currency", "Yalnızca TRY desteklenir.");
            if (row.ActiveMinutes is < 1 or > 10080) Error($"offerings[{n}].activeMinutes", "Etkin süre 1–10080 dakika olmalıdır.");
            if (row.WaitingMinutes is < 0 or > 43200) Error($"offerings[{n}].waitingMinutes", "Bekleme 0–43200 dakika olmalıdır.");
            if (row.Employees is < 1 or > 100) Error($"offerings[{n}].employees", "Çalışan sayısı 1–100 olmalıdır.");
        }
        return errors;
    }
}
