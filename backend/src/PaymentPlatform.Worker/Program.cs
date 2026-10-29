using PaymentPlatform.Application;
using PaymentPlatform.Application.Webhooks;
using PaymentPlatform.Persistence;
using PaymentPlatform.ServiceDefaults;
using PaymentPlatform.Worker.Jobs;

// A slim web host: Kestrel only for the health probes an orchestrator needs, everything else is jobs.
var builder = WebApplication.CreateSlimBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddWorkerJobs();
builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

WebhookOptions.ThrowIfLocalReceiversOutsideDevelopment(builder.Configuration, builder.Environment.IsDevelopment());

var app = builder.Build();

app.MapDefaultEndpoints();

app.Run();
