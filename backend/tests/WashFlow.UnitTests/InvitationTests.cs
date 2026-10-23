using WashFlow.Application.Invitations;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Invitations;
using WashFlow.Infrastructure.Authentication;
using WashFlow.Infrastructure.Email;
using WashFlow.Application.Authentication;
namespace WashFlow.UnitTests;
public sealed class InvitationTests
{
    [Fact]
    public void TokensAreRandomAndPurposeSeparated()
    {
        var generator = new InvitationTokens(); var raw = generator.Generate();
        Assert.True(InvitationService.ValidToken(raw));
        Assert.NotEqual(raw, generator.Generate());
        Assert.NotEqual(generator.Hash(raw), new VerificationTokenGenerator().Hash(raw));
        Assert.DoesNotContain(raw, generator.Hash(raw).ToString());
        Assert.Equal(TimeSpan.FromHours(24), new InvitationOptions().Lifetime);
    }
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("short")] [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void MalformedCredentialsAreRejected(string? raw) => Assert.False(InvitationService.ValidToken(raw));
    [Fact]
    public async Task VerificationAndInvitationMailboxesDoNotOverwriteEachOther()
    {
        var sender = new DevelopmentEmailSender();
        await sender.SendAsync(new() { Recipient = "a@example.test", Token = "verify", ExpiresAt = DateTimeOffset.UtcNow }, default);
        await sender.SendAsync(new() { Recipient = "a@example.test", Token = "invite", ExpiresAt = DateTimeOffset.UtcNow, Purpose = "employee-invitation" }, default);
        Assert.True(sender.TryTake("a@example.test", out var verification)); Assert.Equal("verify", verification!.Token);
        Assert.True(sender.TryTake("a@example.test", "employee-invitation", out var invitation)); Assert.Equal("invite", invitation!.Token);
        Assert.False(sender.TryTake("a@example.test", "employee-invitation", out _));
    }
    [Fact]
    public async Task MalformedAcceptanceNeverTouchesStoreAndRegistrationReturnsTurkishValidation()
    {
        var store = new Store();
        var service = new InvitationService(store, new InvitationTokens(), new(), null!, null!, null!);
        Assert.Equal(InvitationStatus.Invalid, await service.AcceptAsync(Guid.NewGuid(), "bad", default));
        Assert.Equal(0, store.Accepts);
        var result = await service.RegisterAsync(new(), default);
        Assert.Contains("Geçerli", result.Errors!["email"][0]);
        Assert.Contains("password", result.Errors.Keys);
    }
    [Fact]
    public async Task IssuanceNormalizesEmailQueuesRawCredentialAndReturnsOnlyMetadata()
    {
        var store = new Store(); var queue = new Queue(); var tokens = new InvitationTokens();
        var service = new InvitationService(store, tokens, new(), queue, null!, null!);
        var actor = Guid.NewGuid(); var business = Guid.NewGuid();
        var result = await service.IssueAsync(actor, business, " Employee@example.test ", null, default);
        Assert.Equal("EMPLOYEE@EXAMPLE.TEST", store.Email);
        Assert.Equal(actor, store.Actor); Assert.Equal(business, store.Business);
        Assert.Equal(TimeSpan.FromHours(24), store.Lifetime);
        Assert.Equal(tokens.Hash(queue.Message!.Token), store.Hash);
        Assert.Equal("employee-invitation", queue.Message.Purpose);
        Assert.DoesNotContain(queue.Message.Token, result.ToString());
        store.Status = InvitationStatus.Forbidden; queue.Message = null;
        await service.IssueAsync(actor, business, "other@example.test", null, default);
        Assert.Null(queue.Message);
    }
    private sealed class Queue : IVerificationEmailQueue
    {
        public VerificationEmail? Message;
        public bool TryEnqueue(VerificationEmail email) { Message = email; return true; }
    }
    private sealed class Store : IInvitationStore
    {
        public int Accepts; public Guid Actor, Business; public string? Email; public TokenHash? Hash; public TimeSpan Lifetime;
        public InvitationStatus Status = InvitationStatus.Success;
        public Task<InvitationResult> IssueAsync(Guid actor, Guid business, string? email, Guid? resendId, TokenHash hash, TimeSpan lifetime, CancellationToken ct)
        {
            Actor = actor; Business = business; Email = email; Hash = hash; Lifetime = lifetime;
            return Task.FromResult(new InvitationResult(Status, new(Guid.NewGuid(), email!, DateTimeOffset.UtcNow.Add(lifetime), null, null)));
        }
        public Task<InvitationStatus> AcceptAsync(Guid actor, TokenHash hash, CancellationToken ct) { Accepts++; return Task.FromResult(Status); }
        public Task<IReadOnlyList<InvitationView>?> ListAsync(Guid actor, Guid business, CancellationToken ct) => throw new NotImplementedException();
        public Task<InvitationStatus> RevokeAsync(Guid actor, Guid business, Guid invitation, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> RegisterAsync(User user, EmailVerificationToken verification, TokenHash invitation, CancellationToken ct) => throw new NotImplementedException();
    }
}
