namespace WashFlow.Domain.Common;

public interface IEntity
{
    Guid Id { get; }
    DateTimeOffset CreatedAt { get; }
    DateTimeOffset UpdatedAt { get; set; }
}
