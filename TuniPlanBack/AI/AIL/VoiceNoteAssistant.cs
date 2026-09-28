using IAConnectors;
using Microsoft.Extensions.Logging;

namespace AIL;

public sealed record VoiceNoteResult(string Transcript, string Summary);

/// <summary>Turns a business voice note (audio) into text + a short summary for the appointment file.</summary>
public interface IVoiceNoteAssistant
{
    bool IsAvailable { get; }
    Task<VoiceNoteResult> ProcessAsync(Stream audio, string fileName, string contentType, string context, CancellationToken ct = default);
}

public sealed class VoiceNoteAssistant(ISpeechToTextConnector speech, ILlmConnector llm, ILogger<VoiceNoteAssistant> logger) : IVoiceNoteAssistant
{
    private const int FallbackSummaryLength = 280;

    public bool IsAvailable => speech.IsAvailable;

    public async Task<VoiceNoteResult> ProcessAsync(Stream audio, string fileName, string contentType, string context, CancellationToken ct = default)
    {
        var transcript = await speech.TranscribeAsync(audio, fileName, contentType, ct);
        if (string.IsNullOrWhiteSpace(transcript)) return new VoiceNoteResult("", "");
        return new VoiceNoteResult(transcript, await SummarizeAsync(transcript, context, ct));
    }

    private async Task<string> SummarizeAsync(string transcript, string context, CancellationToken ct)
    {
        if (llm.IsAvailable)
        {
            try
            {
                var response = await llm.CompleteAsync(new LlmRequest(
                    "Tu aides un professionnel tunisien à tenir le dossier de ses rendez-vous. " +
                    "Résume sa note vocale en français, en 1 à 3 phrases factuelles (soins faits, points à suivre, prochain rendez-vous). " +
                    "La note peut mélanger français, arabe et darija. Réponds uniquement par le résumé, sans introduction.",
                    [new LlmMessage("user", $"Rendez-vous : {context}\n\nNote vocale :\n{transcript}")],
                    MaxTokens: 300), ct);
                if (!string.IsNullOrWhiteSpace(response.Text)) return response.Text.Trim();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Voice note summary failed, using the beginning of the transcript");
            }
        }
        return transcript.Length <= FallbackSummaryLength ? transcript : transcript[..FallbackSummaryLength].TrimEnd() + "…";
    }
}
