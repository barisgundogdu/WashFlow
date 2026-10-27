using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
namespace WashFlow.Application.Customers;

// Classes avoid generated ToString containing personal customer data.
public sealed class CustomerInput
{
    public string? FullName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public uint? Version { get; set; }
}
public sealed class VehicleInput
{
    public string? Plate { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Category { get; set; }
    public int? ModelYear { get; set; }
    public string? Color { get; set; }
    public string? Notes { get; set; }
    public uint? Version { get; set; }
}
public sealed class CustomerView
{
    public Guid Id { get; init; }
    public Guid BusinessId { get; init; }
    public required string FullName { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Notes { get; init; }
    public uint Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
public sealed class VehicleView
{
    public Guid Id { get; init; }
    public Guid? BusinessId { get; init; }
    public Guid? LocalCustomerId { get; init; }
    public string? CustomerName { get; init; }
    public required string Plate { get; init; }
    public required string Brand { get; init; }
    public required string Model { get; init; }
    public required string Category { get; init; }
    public int? ModelYear { get; init; }
    public string? Color { get; init; }
    public string? Notes { get; init; }
    public uint Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
public sealed class DataPage<T> { public required IReadOnlyList<T> Items { get; init; } public int Page { get; init; } public int PageSize { get; init; } public bool HasMore { get; init; } }
public enum ManagementStatus { Success, Forbidden, NotFound, Conflict }
public sealed record ManagementResult(ManagementStatus Status, object? Value = null);
public interface ICustomerVehicleStore
{
    Task<ManagementResult> Customers(Guid actor, Guid business, string? query, int page, int size, CancellationToken ct);
    Task<ManagementResult> Customer(Guid actor, Guid business, Guid id, CancellationToken ct);
    Task<ManagementResult> SaveCustomer(Guid actor, Guid business, Guid? id, CustomerInput input, CancellationToken ct);
    Task<ManagementResult> Vehicles(Guid actor, Guid? business, Guid? customer, string? query, int page, int size, CancellationToken ct);
    Task<ManagementResult> SaveVehicle(Guid actor, Guid? business, Guid? customer, Guid? id, VehicleInput input, CancellationToken ct);
    Task<ManagementResult> ArchiveVehicle(Guid actor, Guid? business, Guid? customer, Guid id, uint version, CancellationToken ct);
}
public static partial class CustomerVehicleValidation
{
    public static readonly string[] Categories = ["Car", "Suv", "Van", "Pickup", "Motorcycle", "Other"];
    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Normalize(NormalizationForm.FormC);
    public static string SearchKey(string s) => string.Concat(s.Normalize(NormalizationForm.FormC).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    public static string NameSearchKey(string s)=>SearchKey(s).Replace('İ','I').Replace('ı','I');
    public static string PhoneSearchKey(string s) => s.All(c => char.IsAsciiDigit(c) || "+ ()-./".Contains(c)) ? PhoneKey(s) : "";
    public static string PhoneKey(string s) => string.Concat(s.Where(char.IsAsciiDigit));
    public static string PlateDisplay(string s)
    {
        var value = Regex.Replace(s.Trim().Normalize(NormalizationForm.FormC), @"\s+", " ");
        var m = TurkishPlate().Match(value);
        return m.Success ? $"{m.Groups[1].Value} {m.Groups[2].Value.ToUpperInvariant()} {m.Groups[3].Value}" : value;
    }
    [GeneratedRegex(@"^(0[1-9]|[1-7][0-9]|8[01])\s*([A-Za-z]{1,3})\s*([0-9]{2,4})$")]
    private static partial Regex TurkishPlate();
    private static bool Invalid(string? v, int max, bool required = false, bool multiline = false) => (required && string.IsNullOrWhiteSpace(v)) || v?.Length > max || v?.Any(c => char.IsControl(c) && !(multiline && c is '\n' or '\r' or '\t')) == true || v?.Any(char.IsSurrogate) == true;
    public static Dictionary<string, string[]> Customer(CustomerInput i, bool update)
    {
        var e = new Dictionary<string, string[]>(); void Error(string k, string m) => e[k] = [m];
        if (Invalid(i.FullName, 200, true)) Error("fullName", "Ad soyad 1–200 karakter olmalıdır.");
        if (Invalid(i.Phone, 40) || (!string.IsNullOrWhiteSpace(i.Phone) && (PhoneKey(i.Phone).Length == 0 || i.Phone.Any(c => !char.IsAsciiDigit(c) && !"+ ()-./".Contains(c))))) Error("phone", "Geçerli telefon numarası girin.");
        if (Invalid(i.Email, 320) || (!string.IsNullOrWhiteSpace(i.Email) && (!MailAddress.TryCreate(i.Email.Trim(), out var email) || email.Address != i.Email.Trim()))) Error("email", "Geçerli e-posta adresi girin.");
        if (Invalid(i.Notes, 2000, multiline: true)) Error("notes", "Notlar en fazla 2000 karakter olmalıdır.");
        if (update && i.Version is null or 0) Error("version", "Güncel kayıt sürümü gereklidir."); return e;
    }
    public static Dictionary<string, string[]> Vehicle(VehicleInput i, bool update)
    {
        var e = new Dictionary<string, string[]>(); void Error(string k, string m) => e[k] = [m];
        if (Invalid(i.Plate, 32, true) || (!string.IsNullOrWhiteSpace(i.Plate) && (!i.Plate.Any(char.IsLetterOrDigit) || PlateDisplay(i.Plate).Length > 32))) Error("plate", "Geçerli plaka girin (en fazla 32 karakter).");
        if (Invalid(i.Brand, 80, true)) Error("brand", "Marka 1–80 karakter olmalıdır.");
        if (Invalid(i.Model, 100, true)) Error("model", "Model 1–100 karakter olmalıdır.");
        if (i.Category is null || !Categories.Contains(i.Category)) Error("category", "Geçerli araç türü seçin.");
        if (i.ModelYear is < 1886 or > 2100) Error("modelYear", "Model yılı 1886–2100 arasında olmalıdır.");
        if (Invalid(i.Color, 60)) Error("color", "Renk en fazla 60 karakter olmalıdır.");
        if (Invalid(i.Notes, 2000, multiline: true)) Error("notes", "Notlar en fazla 2000 karakter olmalıdır.");
        if (update && i.Version is null or 0) Error("version", "Güncel kayıt sürümü gereklidir."); return e;
    }
    public static bool ValidPage(string? q, int p, int size) => p is >= 1 and <= 10000 && size is >= 1 and <= 50 && !Invalid(q, 100);
}
