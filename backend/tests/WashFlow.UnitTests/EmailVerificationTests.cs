using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WashFlow.Application.Authentication;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Authentication;
using WashFlow.Infrastructure.Email;

namespace WashFlow.UnitTests;

public sealed class EmailVerificationTests
{
    public static EmailVerificationService CreateService() => Service(new Store(), new Queue());
    private static EmailVerificationService Service(Store store, Queue queue) => new(store,
        new VerificationTokenGenerator(), queue, new VerificationOptions(), TimeProvider.System);

    [Fact]
    public void VerificationCredentialsAreRandomPurposeSeparatedAndNotLoggedByToString()
    {
        var generator = new VerificationTokenGenerator();
        var raw = generator.Generate();
        Assert.Equal(43, raw.Length);
        Assert.NotEqual(raw, generator.Generate());
        Assert.NotEqual(generator.Hash(raw), new RefreshTokenGenerator().Hash(raw));
        Assert.Equal(64, generator.Hash(raw).Value.Length);
        Assert.DoesNotContain(raw, new VerificationEmail { Recipient = "a@example.test", Token = raw, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) }.ToString()!);
    }

    [Fact]
    public void RegistrationIssueBindsHashToUserEmailAndSecurityStamp()
    {
        var user = new User("Test", "a@example.test", "encoded-hash");
        var issue = CreateService().CreateIssue(user);
        Assert.Equal(user.Id, issue.Record.UserId);
        Assert.Equal(user.NormalizedEmail, issue.Record.NormalizedEmail);
        Assert.Equal(user.SecurityStamp, issue.Record.SecurityStamp);
        Assert.Equal(new VerificationTokenGenerator().Hash(issue.Email.Token), issue.Record.TokenHash);
        Assert.Equal(TimeSpan.FromMinutes(60), issue.Record.ExpiresAt - issue.Record.CreatedAt);
        Assert.Null(user.EmailVerifiedAt);
    }

    [Fact]
    public async Task UnknownVerifiedThrottledAndEligibleResendsShareAcknowledgement()
    {
        var store = new Store(); var queue = new Queue(); var service = Service(store, queue);
        var unknown = await service.ResendAsync(new() { Email = "a@example.test" }, default);
        Assert.Empty(queue.Messages);
        store.Recipient = new User("Test", "a@example.test", "hash");
        var eligible = await service.ResendAsync(new() { Email = "a@example.test" }, default);
        Assert.Equal(unknown.Value, eligible.Value);
        Assert.Single(queue.Messages);
        store.Recipient = null; // The persistence layer deliberately hides verified/cooldown cases.
        var throttled = await service.ResendAsync(new() { Email = "a@example.test" }, default);
        Assert.Equal(unknown.Value, throttled.Value);
        Assert.Single(queue.Messages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public async Task MalformedVerificationCannotReachPersistence(string? value)
    {
        var store = new Store();
        Assert.False(await Service(store, new Queue()).VerifyAsync(new() { Token = value }, default));
        Assert.Equal(0, store.Verifications);
    }

    [Fact]
    public async Task VerificationHashesRawCredentialBeforePersistence()
    {
        var raw = new VerificationTokenGenerator().Generate();
        var store = new Store { Valid = true };
        Assert.True(await Service(store, new Queue()).VerifyAsync(new() { Token = raw }, default));
        Assert.Equal(new VerificationTokenGenerator().Hash(raw), store.Hash);
    }

    [Fact]
    public async Task InvalidEmailDoesNotQueryPersistence()
    {
        var store = new Store();
        var result = await Service(store, new Queue()).ResendAsync(new() { Email = "invalid" }, default);
        Assert.NotNull(result.Errors);
        Assert.Equal(0, store.Resends);
    }

    [Fact]
    public void ProductionCannotEnableMemoryDeliveryOrOmitProvider()
    {
        var configuration = new ConfigurationManager();
        var environment = new EnvironmentStub { EnvironmentName = Environments.Production };
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddEmailVerification(configuration, environment));
        configuration["Email:Provider"] = "Memory";
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddEmailVerification(configuration, environment));
    }

    [Fact]
    public async Task DevelopmentMailboxKeepsCredentialsOnlyInMemory()
    {
        var services = new ServiceCollection();
        services.AddEmailVerification(new ConfigurationManager(), new EnvironmentStub { EnvironmentName = Environments.Development });
        using var provider = services.BuildServiceProvider();
        var sender = Assert.IsType<DevelopmentEmailSender>(provider.GetRequiredService<IEmailSender>());
        var email = new VerificationEmail { Recipient = "a@example.test", Token = new VerificationTokenGenerator().Generate(), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) };
        await sender.SendAsync(email, default);
        Assert.True(sender.TryTake(email.Recipient, out var received));
        Assert.Same(email, received);
        Assert.False(sender.TryTake(email.Recipient, out _));
    }

    [Fact]
    public async Task ProviderFailureLogsNeitherExceptionNorToken()
    {
        var logger = new CaptureLogger();
        using var dispatcher = new VerificationEmailDispatcher(new FailingSender(), TimeProvider.System, logger);
        var raw = new VerificationTokenGenerator().Generate();
        await dispatcher.StartAsync(default);
        Assert.True(dispatcher.TryEnqueue(new() { Recipient = "a@example.test", Token = raw, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) }));
        await logger.Written.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.StopAsync(default);
        Assert.DoesNotContain(raw, logger.Message!);
        Assert.DoesNotContain("PROVIDER_SECRET", logger.Message!);
        Assert.Null(logger.Exception);
    }

    [Fact]
    public void SmtpConfigurationErrorsNeverEchoCredentials()
    {
        var configuration = new ConfigurationManager();
        configuration["Email:Provider"] = "Smtp";
        configuration["Email:Smtp:Host"] = "smtp.example.test";
        configuration["Email:Smtp:From"] = "noreply@example.test";
        configuration["Email:Smtp:Password"] = "PROVIDER_SECRET";
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddEmailVerification(
            configuration, new EnvironmentStub { EnvironmentName = Environments.Production }));
        Assert.DoesNotContain("PROVIDER_SECRET", error.Message);
    }

    private sealed class FailingSender : IEmailSender
    {
        public Task SendAsync(VerificationEmail email, CancellationToken ct) =>
            throw new InvalidOperationException("PROVIDER_SECRET " + email.Token);
    }
    private sealed class CaptureLogger : ILogger<VerificationEmailDispatcher>
    {
        public TaskCompletionSource Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Message;
        public Exception? Exception;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Message = formatter(state, exception); Exception = exception; Written.TrySetResult(); }
    }

    private sealed class Queue : IVerificationEmailQueue
    {
        public List<VerificationEmail> Messages { get; } = [];
        public bool TryEnqueue(VerificationEmail email) { Messages.Add(email); return true; }
    }
    private sealed class Store : IEmailVerificationStore
    {
        public User? Recipient;
        public TokenHash? Hash;
        public bool Valid;
        public int Verifications;
        public int Resends;
        public Task<VerificationRecipient?> ReplaceAsync(string email, TokenHash hash, DateTimeOffset now, VerificationOptions options, CancellationToken ct)
        { Resends++; return Task.FromResult(Recipient is null ? null : new VerificationRecipient(Recipient, now + options.Lifetime)); }
        public Task<bool> VerifyAsync(TokenHash hash, DateTimeOffset now, CancellationToken ct)
        { Hash = hash; Verifications++; return Task.FromResult(Valid); }
    }
    private sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
