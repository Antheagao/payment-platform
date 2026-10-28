using System.Diagnostics;

namespace PaymentPlatform.Application;

/// <summary>The spans the use cases and adapters emit; the name falls under the <c>PaymentPlatform.*</c> source the hosts export.</summary>
public static class ApplicationTelemetry
{
    public const string SourceName = "PaymentPlatform.Application";

    public static readonly ActivitySource ActivitySource = new(SourceName);
}
