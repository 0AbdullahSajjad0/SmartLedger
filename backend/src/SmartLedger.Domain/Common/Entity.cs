namespace SmartLedger.Domain.Common;

/// <summary>
/// Base class for every persisted entity. CreatedAt and UpdatedAt are set by the persistence layer on save.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
