using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace PaymentPlatform.Application.Webhooks;

/// <summary>One signed delivery attempt, ready to send.</summary>
public sealed record WebhookRequest(Uri Url, string Id, long Timestamp, string Body, string Signature, string? CorrelationId);

/// <summary>What one attempt got: the receiver's status code, or none and an error when no answer came back.</summary>
public sealed record WebhookAttempt(int? StatusCode, string? Error)
{
    public bool Delivered => StatusCode is >= 200 and <= 299;
}

/// <summary>The webhook transport, a port so tests can answer for merchants' servers.</summary>
public interface IWebhookSender
{
    Task<WebhookAttempt> SendAsync(WebhookRequest request, CancellationToken cancellationToken);
}

/// <summary>Thrown by the connection guard when every address a webhook host resolves to is refused.</summary>
public sealed class WebhookAddressRefusedException(string host)
    : Exception($"{host} does not resolve only to public addresses");

/// <summary>
/// Sends webhooks over HTTP with the SSRF rules applied at connect time (security overlay): the connection callback
/// resolves the host itself and connects only to addresses <see cref="WebhookUrlPolicy.ResolveAllowedAsync"/> allows,
/// so a name that points somewhere private by the time of the send reaches nothing. No proxy, no redirects, a 10 s
/// limit per attempt, and the response body is never read.
/// </summary>
public sealed class HttpWebhookSender(IHttpClientFactory clients) : IWebhookSender
{
    public const string ClientName = "webhooks";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<WebhookAttempt> SendAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, request.Url)
        {
            Content = new StringContent(request.Body, Encoding.UTF8, "application/json"),
        };
        message.Headers.Add(WebhookSignature.IdHeader, request.Id);
        message.Headers.Add(WebhookSignature.TimestampHeader, request.Timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        message.Headers.Add(WebhookSignature.SignatureHeader, request.Signature);
        if (request.CorrelationId is not null)
        {
            message.Headers.Add("X-Correlation-Id", request.CorrelationId);
        }

        var client = clients.CreateClient(ClientName);
        try
        {
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return new WebhookAttempt((int)response.StatusCode, response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebhookAttempt(null, $"no answer within {Timeout.TotalSeconds:0} s");
        }
        catch (HttpRequestException exception) when (exception.InnerException is WebhookAddressRefusedException refused)
        {
            return new WebhookAttempt(null, refused.Message);
        }
        catch (HttpRequestException exception)
        {
            return new WebhookAttempt(null, exception.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => "the host did not resolve",
                HttpRequestError.ConnectionError => "the connection failed",
                HttpRequestError.SecureConnectionError => "the TLS handshake failed",
                _ => "the request failed: " + exception.Message,
            });
        }
    }

    /// <summary>Registers the named client with its guarded handler.</summary>
    internal static void AddTo(IServiceCollection services)
    {
        services.AddHttpClient(ClientName, client =>
            {
                client.Timeout = Timeout;
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PaymentPlatform-Webhooks", "1.0"));
            })
            .ConfigurePrimaryHttpMessageHandler(services => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                UseCookies = false,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectCallback = (context, cancellationToken) =>
                    ConnectAsync(services.GetRequiredService<WebhookUrlPolicy>(), context.DnsEndPoint, cancellationToken),
            });
        services.AddSingleton<IWebhookSender, HttpWebhookSender>();
    }

    /// <summary>Connects to the first allowed address that answers; the name is never resolved anywhere else.</summary>
    internal static async ValueTask<Stream> ConnectAsync(WebhookUrlPolicy policy, DnsEndPoint endpoint, CancellationToken cancellationToken)
    {
        var addresses = await policy.ResolveAllowedAsync(endpoint.Host, cancellationToken);
        if (addresses.Length == 0)
        {
            throw new WebhookAddressRefusedException(endpoint.Host);
        }

        SocketException? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                last = exception;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw last!;
    }
}
