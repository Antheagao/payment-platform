using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Timeouts;
using PaymentPlatform.Api;
using PaymentPlatform.Api.Auth;
using PaymentPlatform.Api.Endpoints;
using PaymentPlatform.Api.Errors;
using PaymentPlatform.Application;
using PaymentPlatform.Application.Demo;
using PaymentPlatform.Persistence;
using PaymentPlatform.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddApplication();
var usesEphemeralSigningKey = builder.AddApiAuthentication();

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    // Payment requests are small; a cap keeps a slow or huge body from tying up the server.
    kestrel.Limits.MaxRequestBodySize = 64 * 1024;
});
builder.Services.AddRequestTimeouts(options => options.DefaultPolicy = new RequestTimeoutPolicy
{
    Timeout = TimeSpan.FromSeconds(10),
    TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable,
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});
builder.Services.AddApiProblemDetails();
// A body that does not bind is a 400 problem in every environment; by default Development throws instead.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseCorrelationIds();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseRequestTimeouts();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapDefaultEndpoints();
app.MapApiV1().MapAuthEndpoints().MapPaymentEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (usesEphemeralSigningKey)
{
    AuthenticationSetup.LogEphemeralSigningKey(app.Logger);
}

if (app.Configuration.GetValue<bool>(DatabaseMigrator.SettingName))
{
    await DatabaseMigrator.MigrateAsync(app.Services, app.Lifetime.ApplicationStopping);
}

if (app.Configuration.GetValue<bool>(DemoSeed.SettingName))
{
    // The demo login is public, so the setting is a startup error anywhere it could reach real merchants.
    if (app.Environment.IsProduction())
    {
        throw new InvalidOperationException($"{DemoSeed.SettingName} is refused in Production: the demo login is public.");
    }

    await DemoSeed.RunAsync(app.Services, app.Lifetime.ApplicationStopping);
}

await app.RunAsync();
