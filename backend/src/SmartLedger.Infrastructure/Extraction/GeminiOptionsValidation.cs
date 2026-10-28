using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SmartLedger.Infrastructure.Extraction;

/// <summary>
/// Outside development the API key must be set, or the app refuses to start, rather than silently leaving every
/// receipt unread. In development it's optional: without it, receipts are marked unreadable with a clear reason.
/// </summary>
internal sealed class GeminiOptionsValidation(IHostEnvironment environment) : IValidateOptions<GeminiOptions>
{
    public ValidateOptionsResult Validate(string? name, GeminiOptions options) =>
        !environment.IsDevelopment() && string.IsNullOrWhiteSpace(options.ApiKey)
            ? ValidateOptionsResult.Fail("Gemini:ApiKey is required. Set it as a Container App secret (env Gemini__ApiKey).")
            : ValidateOptionsResult.Success;
}
