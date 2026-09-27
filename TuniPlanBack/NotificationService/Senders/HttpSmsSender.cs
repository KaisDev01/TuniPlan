using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NotificationService.Senders;

/// <summary>
/// Generic HTTP SMS gateway. Adapt the payload to your Tunisian SMS provider
/// (the request below is a common JSON shape: { from, to, text }).
/// </summary>
public sealed class HttpSmsSender(HttpClient http, IOptions<NotificationOptions> options, ILogger<HttpSmsSender> logger) : ISmsSender
{
    public async Task<bool> SendAsync(OutgoingMessage message, CancellationToken ct = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.SmsApiUrl)) throw new InvalidOperationException("Notifications:SmsApiUrl is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, o.SmsApiUrl)
        {
            Content = JsonContent.Create(new { from = o.SmsSenderName, to = message.To, text = message.Body })
        };
        request.Headers.Add("Authorization", $"Bearer {o.SmsApiKey}");
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("SMS provider returned {Status} for {To}", (int)response.StatusCode, message.To);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "SMS sending failed for {To}", message.To);
            return false;
        }
    }
}
