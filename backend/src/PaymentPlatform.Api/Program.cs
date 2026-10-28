using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Timeouts;
using PaymentPlatform.Api;
using PaymentPlatform.Api.Endpoints;
using PaymentPlatform.Api.Errors;
using PaymentPlatform.Application;
using PaymentPlatform.Persistence;
using PaymentPlatform.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddApplication();

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

app.MapDefaultEndpoints();
app.MapApiV1();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();
