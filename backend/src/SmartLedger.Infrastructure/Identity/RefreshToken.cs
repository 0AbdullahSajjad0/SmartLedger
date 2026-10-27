namespace SmartLedger.Infrastructure.Identity;

/// <summary>
/// A long-lived token that can be exchanged once for new tokens. Only its SHA-256 hash is stored,
/// so a database leak doesn't expose usable tokens.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public required string TokenHash { get; set; }

    /// <summary>Shared by every token descended from one sign-in, so a stolen chain can be revoked together.</summary>
    public Guid FamilyId { get; set; }

    /// <summary>Whether the sign-in asked to stay signed in longer; carried over on every refresh.</summary>
    public bool RememberMe { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>The token issued when this one was used. Set means "already used".</summary>
    public Guid? ReplacedByTokenId { get; set; }
}
