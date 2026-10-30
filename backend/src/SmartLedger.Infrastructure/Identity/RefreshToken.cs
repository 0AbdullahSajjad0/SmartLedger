namespace SmartLedger.Infrastructure.Identity;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public required string TokenHash { get; set; }

    public Guid FamilyId { get; set; }

    public bool RememberMe { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? ReplacedByTokenId { get; set; }
}
