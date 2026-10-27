using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using SmartLedger.Application.Common;

namespace SmartLedger.Api.Auth;

/// <summary>Reads the user id from the validated access token's "sub" claim.</summary>
internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            ? id
            : null;
}
