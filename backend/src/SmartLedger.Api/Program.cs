using Scalar.AspNetCore;
using SmartLedger.Api.ExceptionHandling;
using SmartLedger.Application;
using SmartLedger.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Each layer registers its own services.
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Errors are returned as ProblemDetails JSON.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
