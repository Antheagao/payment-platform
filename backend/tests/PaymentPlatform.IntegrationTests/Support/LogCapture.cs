using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace PaymentPlatform.IntegrationTests.Support;

/// <summary>Every log record an API host writes, at Trace and up, for tests that prove what is never logged.</summary>
public static class LogCapture
{
    public static WebApplicationFactory<Program> Attach(WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddFakeLogging();
        }));
    }

    /// <summary>Each record's message and the values of its structured state, one line per record.</summary>
    public static IReadOnlyList<string> Lines(WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.Services.GetFakeLogCollector().GetSnapshot()
            .Select(record => $"{record.Message} {string.Join(' ', record.StructuredState?.Select(pair => pair.Value) ?? [])} {record.Exception}")
            .ToList();
    }
}
