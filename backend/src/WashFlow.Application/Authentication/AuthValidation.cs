using System.Net.Mail;

namespace WashFlow.Application.Authentication;

public static class AuthValidation
{
    public static Dictionary<string, string[]> Registration(RegistrationRequest request, bool business)
    {
        var errors = Credentials(request.Email, request.Password);
        Name(errors, business ? "ownerFullName" : "fullName", business ? request.OwnerFullName : request.FullName, 200);
        if (business) Name(errors, "businessName", request.BusinessName, 200);
        return errors;
    }
    public static Dictionary<string, string[]> Credentials(string? email, string? password)
    {
        var errors = new Dictionary<string, string[]>();
        var trimmed = email?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 320 ||
            !MailAddress.TryCreate(trimmed, out var address) || address.Address != trimmed)
            errors["email"] = ["Geçerli bir e-posta adresi girin."];
        // Do not invent a complexity policy; bound work/input and preserve the password exactly.
        if (string.IsNullOrWhiteSpace(password) || password.Length > 1024)
            errors["password"] = ["Şifre boş olamaz ve 1024 karakteri aşamaz."];
        return errors;
    }
    public static void Name(Dictionary<string, string[]> errors, string key, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max)
            errors[key] = [$"Bu alan zorunludur ve {max} karakteri aşamaz."];
    }
}
