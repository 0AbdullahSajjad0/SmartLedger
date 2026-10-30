using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using SmartLedger.Api.Auth;
using SmartLedger.Api.Common;
using SmartLedger.Application.Account;
using SmartLedger.Application.Auth;
using SmartLedger.Application.Common;

namespace SmartLedger.Api.Controllers;

[ApiController]
[Route("api/account")]
public sealed class AccountController(IAccountService account, IAccountExportService exports, ICurrentUser currentUser)
    : ControllerBase
{
    private Guid UserId => currentUser.UserId!.Value;

    [HttpPut]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        (await account.UpdateProfileAsync(UserId, request, cancellationToken)).ToActionResult(this);

    [HttpPut("notifications")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateNotifications(UpdateNotificationsRequest request, CancellationToken cancellationToken) =>
        (await account.UpdateNotificationsAsync(UserId, request, cancellationToken)).ToActionResult(this);

    [HttpPost("confirmation-email")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> SendConfirmationEmail(CancellationToken cancellationToken)
    {
        var result = await account.SendConfirmationEmailAsync(UserId, cancellationToken);
        return result.IsSuccess ? Accepted() : result.ToActionResult(this);
    }

    [HttpPost("password")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await account.ChangePasswordAsync(UserId, request, cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToActionResult(this);
    }

    [HttpPost("sign-out-others")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SignOutOthers(SignOutOthersRequest request, CancellationToken cancellationToken)
    {
        await account.SignOutOtherDevicesAsync(UserId, request.RefreshToken, cancellationToken);
        return NoContent();
    }

    [HttpGet("export")]
    [EnableRateLimiting(RateLimitPolicies.Exports)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileStreamResult))]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var result = await exports.PrepareAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ToActionResult(this);
        }

        Response.ContentType = "application/zip";
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = result.Value.FileName,
        }.ToString();

        await result.Value.WriteToAsync(Response.Body, cancellationToken);
        return new EmptyResult();
    }

    [HttpPost("delete")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        var result = await account.DeleteAsync(UserId, request, cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToActionResult(this);
    }
}
