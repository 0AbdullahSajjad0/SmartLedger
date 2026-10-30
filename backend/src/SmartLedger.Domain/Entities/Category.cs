using SmartLedger.Domain.Common;

namespace SmartLedger.Domain.Entities;

public class Category : Entity
{
    public const int MaxNameLength = 60;

    public Guid UserId { get; set; }

    public required string Name { get; set; }

    public required string Color { get; set; }

    public bool IsArchived { get; set; }
}
