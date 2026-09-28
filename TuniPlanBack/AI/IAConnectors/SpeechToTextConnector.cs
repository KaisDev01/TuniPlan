using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IAConnectors;

/// <summary>Speech-to-text provider used for business voice notes.</summary>
public interface ISpeechToTextConnector
{
    /// <summary>False when no transcription provider is configured.</summary>
    bool IsAvailable { get; }
    Task<string> TranscribeAsync(Stream audio, string fileName, string contentType, CancellationToken ct = default);
}

public sealed class TranscriptionOptions
{
    public const string Section = "AI:Transcription";
    /// <summary>"None" or "OpenAI" (any OpenAI-compatible /v1/audio/transcriptions endpoint: OpenAI Whisper, Groq, a local whisper server...).</summary>
    public string Provider { get; set; } = "None";
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://api.openai.com";
    public string Model { get; set; } = "whisper-1";
    /// <summary>Optional ISO-639-1 hint ("fr", "ar"). Empty = automatic detection (better for mixed darija / French).</summary>
    public string? Language { get; set; }
}

public sealed class NoSpeechToTextConnector : ISpeechToTextConnector
{
    public bool IsAvailable => false;
    public Task<string> TranscribeAsync(Stream audio, string fileName, string contentType, CancellationToken ct = default) =>
        throw new InvalidOperationException("No transcription provider configured.");
}

/// <summary>OpenAI-compatible transcription API (POST {BaseUrl}/v1/audio/transcriptions, multipart).</summary>
public sealed class OpenAiSpeechToTextConnector(HttpClient http, IOptions<TranscriptionOptions> options, ILogger<OpenAiSpeechToTextConnector> logger)
    : ISpeechToTextConnector
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public async Task<string> TranscribeAsync(Stream audio, string fileName, string contentType, CancellationToken ct = default)
    {
        var o = options.Value;
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", string.IsNullOrWhiteSpace(fileName) ? "note.m4a" : Path.GetFileName(fileName));
        form.Add(new StringContent(o.Model), "model");
        if (!string.IsNullOrWhiteSpace(o.Language)) form.Add(new StringContent(o.Language), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl.TrimEnd('/')}/v1/audio/transcriptions") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Transcription failed: {Status} {Body}", (int)response.StatusCode, error.Length > 500 ? error[..500] : error);
            throw new HttpRequestException($"Transcription provider returned {(int)response.StatusCode}.");
        }
        var payload = await response.Content.ReadFromJsonAsync<TranscriptionResponse>(cancellationToken: ct);
        return payload?.Text?.Trim() ?? "";
    }

    private sealed class TranscriptionResponse
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
    }
}
