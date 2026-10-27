using System.Net.Mail;

namespace WashFlow.Application.Businesses;

public sealed record OpeningDay(int Day, bool Closed, int? OpensAtMinute, int? ClosesAtMinute);
public class BusinessSettingsInput
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? ContactEmail { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? District { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
    public string? Description { get; set; }
    public string? TimeZoneId { get; set; }
    public IReadOnlyList<OpeningDay>? WeeklyHours { get; set; }
    public uint? Version { get; set; }
}
public sealed class BusinessSettingsView : BusinessSettingsInput { public Guid BusinessId { get; set; } }
public enum SettingsStatus { Success, Forbidden, Conflict }
public sealed record SettingsResult(SettingsStatus Status, BusinessSettingsView? Value = null);
public interface IBusinessSettingsStore
{
    Task<SettingsResult> ReadAsync(Guid actor, Guid business, CancellationToken ct);
    Task<SettingsResult> UpdateAsync(Guid actor, Guid business, BusinessSettingsInput input, CancellationToken ct);
}
public static class BusinessSettingsValidation
{
    public static Dictionary<string, string[]> Validate(BusinessSettingsInput r)
    {
        var errors = new Dictionary<string, string[]>();
        void Error(string key, string message) => errors[key] = [message];
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 200) Error("name", "İşletme adı 1–200 karakter olmalıdır.");
        if (r.Version is null or 0) Error("version", "Güncel kayıt sürümü gereklidir.");
        if (r.Phone?.Length > 40) Error("phone", "Alan en fazla 40 karakter olabilir.");
        if (r.ContactEmail?.Length > 320) Error("contactEmail", "Alan en fazla 320 karakter olabilir.");
        if (r.AddressLine1?.Length > 250) Error("addressLine1", "Alan en fazla 250 karakter olabilir.");
        if (r.AddressLine2?.Length > 250) Error("addressLine2", "Alan en fazla 250 karakter olabilir.");
        if (r.District?.Length > 100) Error("district", "Alan en fazla 100 karakter olabilir.");
        if (r.City?.Length > 100) Error("city", "Alan en fazla 100 karakter olabilir.");
        if (r.Region?.Length > 100) Error("region", "Alan en fazla 100 karakter olabilir.");
        if (r.PostalCode?.Length > 32) Error("postalCode", "Alan en fazla 32 karakter olabilir.");
        if (r.CountryCode?.Length > 2) Error("countryCode", "Alan en fazla 2 karakter olabilir.");
        if (r.Description?.Length > 2000) Error("description", "Alan en fazla 2000 karakter olabilir.");
        if (r.TimeZoneId?.Length > 100) Error("timeZoneId", "Alan en fazla 100 karakter olabilir.");
        if (!string.IsNullOrWhiteSpace(r.Phone) && (!r.Phone.Any(char.IsAsciiDigit) || r.Phone.Any(c => !char.IsAsciiDigit(c) && !"+ ()-./".Contains(c)))) Error("phone", "Geçerli bir telefon numarası girin.");
        if (!string.IsNullOrWhiteSpace(r.ContactEmail) && (!MailAddress.TryCreate(r.ContactEmail, out var address) || address.Address != r.ContactEmail)) Error("contactEmail", "Geçerli bir e-posta adresi girin.");
        if (!string.IsNullOrWhiteSpace(r.CountryCode) && (r.CountryCode.Length != 2 || !r.CountryCode.All(char.IsAsciiLetter))) Error("countryCode", "İki harfli ülke kodu girin.");
        if (!string.IsNullOrWhiteSpace(r.TimeZoneId) && !ValidTimeZone(r.TimeZoneId)) Error("timeZoneId", "Geçerli bir IANA saat dilimi girin.");
        foreach (var (key, value) in new (string, string?)[] { ("name", r.Name), ("phone", r.Phone), ("contactEmail", r.ContactEmail), ("addressLine1", r.AddressLine1), ("addressLine2", r.AddressLine2), ("district", r.District), ("city", r.City), ("region", r.Region), ("postalCode", r.PostalCode), ("countryCode", r.CountryCode), ("timeZoneId", r.TimeZoneId), ("description", r.Description) })
            if (value?.Any(c => char.IsControl(c) && !(key == "description" && c is '\n' or '\r' or '\t')) == true) Error(key, "Geçersiz kontrol karakteri içeriyor.");
        if (r.WeeklyHours is { } days)
        {
            if (string.IsNullOrWhiteSpace(r.TimeZoneId)) Error("timeZoneId", "Çalışma saatleri için saat dilimi gereklidir.");
            if (days.Count != 7 || days.Any(x => x is null) || days.Select(x => x.Day).Distinct().Count() != 7 || days.Any(x => x.Day < 1 || x.Day > 7 ||
                (x.Closed ? x.OpensAtMinute != null || x.ClosesAtMinute != null : x.OpensAtMinute is null || x.ClosesAtMinute is null || x.OpensAtMinute < 0 || x.OpensAtMinute >= x.ClosesAtMinute || x.ClosesAtMinute > 1440)))
                Error("weeklyHours", "Yedi günün her birini bir kez belirtin. Açılış kapanıştan önce olmalı; kapalı günlerde saat olmamalıdır.");
        }
        return errors;
    }
    public static bool ValidTimeZone(string id) => id == "Etc/UTC" || (id.Contains('/') && TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out _));
}
