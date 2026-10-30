using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SmartLedger.Infrastructure.Extraction;

internal sealed class GeminiOptionsValidation(IHostEnvironment environment) : IValidateOptions<GeminiOptions>
{
    public ValidateOptionsResult Validate(string? name, GeminiOptions options) =>
        !environment.IsDevelopment() && string.IsNullOrWhiteSpace(options.ApiKey)
            ? ValidateOptionsResult.Fail("Gemini:ApiKey is required. Set it as a Container App secret (env Gemini__ApiKey).")
            : ValidateOptionsResult.Success;
}
