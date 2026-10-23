using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WashFlow.Application.Authentication;
using WashFlow.Infrastructure.Authentication;

namespace WashFlow.Infrastructure.Email;

public static class DependencyInjection
{
    public static IServiceCollection AddEmailVerification(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var lifetime = ReadRange(configuration, "Verification:LifetimeMinutes", 60, 1, 1440);
        var cooldown = ReadRange(configuration, "Verification:ResendCooldownSeconds", 60, 1, 3600);
        services.AddSingleton(new VerificationOptions { Lifetime = TimeSpan.FromMinutes(lifetime), ResendCooldown = TimeSpan.FromSeconds(cooldown) });
        services.AddSingleton<IVerificationTokenGenerator, VerificationTokenGenerator>();
        services.AddScoped<IEmailVerificationStore, EfEmailVerificationStore>();
        var provider = configuration["Email:Provider"] ?? (environment.IsDevelopment() ? "Memory" : "");
        if (provider == "Memory" && environment.IsDevelopment())
        {
            services.AddSingleton<DevelopmentEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<DevelopmentEmailSender>());
        }
        else if (provider == "Smtp")
        {
            var host = configuration["Email:Smtp:Host"];
            var from = configuration["Email:Smtp:From"];
            var username = configuration["Email:Smtp:Username"];
            var password = configuration["Email:Smtp:Password"];
            if (string.IsNullOrWhiteSpace(host) || !MailAddress.TryCreate(from, out _) ||
                string.IsNullOrEmpty(username) != string.IsNullOrEmpty(password))
                throw new InvalidOperationException("Email__Smtp yapılandırması geçersiz.");
            var port = ReadRange(configuration, "Email:Smtp:Port", 587, 1, 65535);
            if (port == 465) throw new InvalidOperationException("SMTP sağlayıcısı STARTTLS bağlantı noktası gerektirir.");
            services.AddSingleton(new SmtpSettings { Host = host, From = from!, Port = port, Username = username, Password = password });
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else throw new InvalidOperationException("Üretimde Email__Provider=Smtp gereklidir; Memory yalnızca Development ortamında kullanılabilir.");
        services.AddSingleton<VerificationEmailDispatcher>();
        services.AddSingleton<IVerificationEmailQueue>(sp => sp.GetRequiredService<VerificationEmailDispatcher>());
        services.AddHostedService(sp => sp.GetRequiredService<VerificationEmailDispatcher>());
        return services;
    }
    private static int ReadRange(IConfiguration configuration, string key, int fallback, int min, int max)
    {
        if (configuration[key] is null) return fallback;
        if (int.TryParse(configuration[key], out var value) && value >= min && value <= max) return value;
        throw new InvalidOperationException($"{key} yapılandırması geçersiz.");
    }
}
