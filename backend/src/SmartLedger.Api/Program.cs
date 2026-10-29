using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.JsonWebTokens;
using Scalar.AspNetCore;
using SmartLedger.Api.Auth;
using SmartLedger.Api.ExceptionHandling;
using SmartLedger.Api.Security;
using SmartLedger.Application;
using SmartLedger.Application.Common;
using SmartLedger.Infrastructure;
using SmartLedger.Infrastructure.Persistence;
using SmartLedger.Infrastructure.Previews;

// CI runs the built Docker image with this flag before deploying it, to prove the native image libraries work
// inside the minimal production container. It needs no configuration and exits straight after.
if (args is [ReceiptPreviewSelfCheck.CommandLineFlag])
{
    Environment.ExitCode = await ReceiptPreviewSelfCheck.RunAsync(Console.Out) ? 0 : 1;
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Each layer registers its own services.
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

// Enums travel as names ("NeedsReview"), not numbers, so the API is readable and adding a value can't shift others.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// Errors are returned as ProblemDetails JSON.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHealthChecks();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Every endpoint requires a signed-in user unless it opts out with [AllowAnonymous] / AllowAnonymous().
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:AuthPermitLimit", 10),
            Window = TimeSpan.FromMinutes(1),
        }));
    options.AddPolicy(RateLimitPolicies.Uploads, context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:UploadPermitLimit", 20),
            Window = TimeSpan.FromMinutes(1),
        }));
    options.AddPolicy(RateLimitPolicies.Insights, context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:InsightPermitLimit", 10),
            Window = TimeSpan.FromHours(1),
        }));
});

// Azure Container Apps' ingress terminates HTTPS and forwards the request; trust its headers so the app
// sees the real client IP (for rate limiting) and the original https scheme. Nothing else can reach the container.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Only the Angular app's origins may call the API from a browser. Set per environment in configuration.
const string FrontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:InitializeOnStartup"))
{
    await app.Services.InitializeDevelopmentDatabaseAsync();
}

app.UseForwardedHeaders();
app.UseSecurityHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.UseHttpsRedirection();

// Must run before authorization so browser preflight (OPTIONS) requests are answered.
app.UseCors(FrontendCorsPolicy);

// Authentication first, so per-user rate limits know who the user is.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

// Liveness: the process is up. Never checks the database, so a paused database can't get a healthy container restarted.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

// Readiness: dependencies such as the database are reachable.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.Run();
