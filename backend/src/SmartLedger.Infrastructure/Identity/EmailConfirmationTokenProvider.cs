using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SmartLedger.Infrastructure.Identity;

/// <summary>
/// Signs "confirm your email" links. A provider of its own so these links can last days while password reset links
/// last an hour: a reset link is a key to the account, a confirmation link only proves the inbox is yours.
/// </summary>
internal sealed class EmailConfirmationTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailConfirmationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger)
{
    public const string ProviderName = "EmailConfirmation";
}

internal sealed class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(3);

    public EmailConfirmationTokenProviderOptions()
    {
        Name = EmailConfirmationTokenProvider.ProviderName;
        TokenLifespan = Lifetime;
    }
}
