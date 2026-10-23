namespace WashFlow.Application.Authorization;

public sealed class UserContextService(ICurrentUserContextStore store)
{
    public Task<UserContexts> GetAsync(Guid userId, CancellationToken ct) =>
        userId == Guid.Empty
            ? throw new ArgumentException("Geçerli kullanıcı gereklidir.", nameof(userId))
            : store.GetAsync(userId, ct);
}
