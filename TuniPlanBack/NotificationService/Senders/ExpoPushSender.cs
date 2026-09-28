using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NotificationService.Senders;

/// <summary>Expo push service (https://docs.expo.dev/push-notifications/sending-notifications/).</summary>
public sealed class ExpoPushSender(HttpClient http, IOptions<NotificationOptions> options, ILogger<ExpoPushSender> logger) : IPushSender
{
    private const int BatchSize = 100; // Expo limit per request

    public async Task<IReadOnlyList<string>> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken ct = default)
    {
        var o = options.Value;
        var invalid = new List<string>();
        foreach (var batch in tokens.Distinct().Chunk(BatchSize))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, o.ExpoPushUrl)
            {
                Content = JsonContent.Create(batch.Select(token => new
                {
                    to = token, title = message.Title, body = message.Body, data = message.Data, sound = "default", priority = "high"
                }))
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(o.ExpoAccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ExpoAccessToken);

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Expo push failed: HTTP {Status}", (int)response.StatusCode);
                continue;
            }

            // One ticket per message, in the same order as the request
            var payload = await response.Content.ReadFromJsonAsync<ExpoResponse>(cancellationToken: ct);
            var tickets = payload?.Data ?? [];
            for (var i = 0; i < tickets.Count && i < batch.Length; i++)
            {
                if (tickets[i].Status != "error") continue;
                if (tickets[i].Details?.Error == "DeviceNotRegistered") invalid.Add(batch[i]);
                else logger.LogWarning("Expo push ticket error: {Message}", tickets[i].Message);
            }
        }
        return invalid;
    }

    private sealed class ExpoResponse
    {
        [JsonPropertyName("data")] public List<Ticket>? Data { get; set; }
    }

    private sealed class Ticket
    {
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("details")] public TicketDetails? Details { get; set; }
    }

    private sealed class TicketDetails
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}
