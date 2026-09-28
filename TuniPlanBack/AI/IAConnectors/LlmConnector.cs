using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IAConnectors;

public sealed record LlmMessage(string Role, string Content);

public sealed record LlmRequest(string SystemPrompt, IReadOnlyList<LlmMessage> Messages, int MaxTokens = 800);

public sealed record LlmResponse(string Text);

/// <summary>Connector to a large language model provider.</summary>
public interface ILlmConnector
{
    string Name { get; }
    /// <summary>False when no API key is configured: the AI layer then uses its rule-based fallback.</summary>
    bool IsAvailable { get; }
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}

public sealed class AiOptions
{
    public const string Section = "AI";
    /// <summary>"None" (rule-based only) or "Anthropic".</summary>
    public string Provider { get; set; } = "None";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-haiku-4-5-20251001";
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
}

public sealed class NoLlmConnector : ILlmConnector
{
    public string Name => "none";
    public bool IsAvailable => false;
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default) =>
        throw new InvalidOperationException("No LLM provider configured.");
}

/// <summary>Anthropic Messages API connector (POST /v1/messages).</summary>
public sealed class AnthropicConnector(HttpClient http, IOptions<AiOptions> options, ILogger<AnthropicConnector> logger) : ILlmConnector
{
    public string Name => "anthropic";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var o = options.Value;
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl.TrimEnd('/')}/v1/messages")
        {
            Content = JsonContent.Create(new
            {
                model = o.Model,
                max_tokens = request.MaxTokens,
                system = request.SystemPrompt,
                messages = request.Messages.Select(m => new { role = m.Role, content = m.Content })
            })
        };
        message.Headers.Add("x-api-key", o.ApiKey);
        message.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("LLM call failed: {Status} {Body}", (int)response.StatusCode, error.Length > 500 ? error[..500] : error);
            throw new HttpRequestException($"LLM provider returned {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadFromJsonAsync<AnthropicResponse>(cancellationToken: ct);
        var text = string.Join("", payload?.Content?.Where(c => c.Type == "text").Select(c => c.Text) ?? []);
        return new LlmResponse(text);
    }

    private sealed class AnthropicResponse
    {
        [JsonPropertyName("content")] public List<ContentBlock>? Content { get; set; }
    }

    private sealed class ContentBlock
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("text")] public string? Text { get; set; }
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddAiConnectors(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AiOptions.Section);
        services.Configure<AiOptions>(section);
        if (string.Equals(section["Provider"], "Anthropic", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<ILlmConnector, AnthropicConnector>(c => c.Timeout = TimeSpan.FromSeconds(30));
        else
            services.AddSingleton<ILlmConnector, NoLlmConnector>();

        var transcription = configuration.GetSection(TranscriptionOptions.Section);
        services.Configure<TranscriptionOptions>(transcription);
        if (string.Equals(transcription["Provider"], "OpenAI", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<ISpeechToTextConnector, OpenAiSpeechToTextConnector>(c => c.Timeout = TimeSpan.FromSeconds(60));
        else
            services.AddSingleton<ISpeechToTextConnector, NoSpeechToTextConnector>();
        return services;
    }

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
