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

if (args is [ReceiptPreviewSelfCheck.CommandLineFlag])
{
    Environment.ExitCode = await ReceiptPreviewSelfCheck.RunAsync(Console.Out) ? 0 : 1;
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHealthChecks();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

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
    options.AddPolicy(RateLimitPolicies.Exports, context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:ExportPermitLimit", 5),
            Window = TimeSpan.FromHours(1),
        }));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

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

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.Run();
