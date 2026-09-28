using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IAConnectors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIL;

public sealed record AiService(Guid Id, string Name, int DurationMinutes, decimal Price);

public sealed record AiSlot(int Index, DateTime StartUtc, DateTime LocalStart, Guid? ResourceId);

public sealed record SecretaryContext(
    string OrganizationName,
    string? OrganizationDescription,
    bool ManualValidation,
    IReadOnlyList<AiService> Services,
    AiService SelectedService,
    IReadOnlyList<AiSlot> AvailableSlots,
    DateOnly TodayLocal);

public sealed record SecretaryTurn(bool FromUser, string Content);

public sealed record SecretaryResult(string Reply, IReadOnlyList<int> ProposedSlotIndexes, Guid? SuggestedServiceId);

/// <summary>
/// The "secrétaire IA". Golden rule (see roadmap): the AI only reads availability and proposes slots.
/// It never confirms an appointment; the client creates a request and the business owner confirms it.
/// </summary>
public interface ISecretaryAgent
{
    /// <summary>Picks the service that best matches the client's words (null = no clear match).</summary>
    Guid? MatchService(string message, IReadOnlyList<AiService> services);
    Task<SecretaryResult> RespondAsync(SecretaryContext context, IReadOnlyList<SecretaryTurn> history, string userMessage, CancellationToken ct = default);
}

public sealed partial class SecretaryAgent(ILlmConnector llm, ILogger<SecretaryAgent> logger) : ISecretaryAgent
{
    public Guid? MatchService(string message, IReadOnlyList<AiService> services)
    {
        var text = Normalize(message);
        AiService? best = null;
        var bestScore = 0;
        foreach (var s in services)
        {
            var words = Normalize(s.Name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3);
            var score = words.Count(w => text.Contains(w));
            if (score > bestScore) { bestScore = score; best = s; }
        }
        return best?.Id;
    }

    public async Task<SecretaryResult> RespondAsync(SecretaryContext context, IReadOnlyList<SecretaryTurn> history, string userMessage, CancellationToken ct = default)
    {
        if (llm.IsAvailable)
        {
            try
            {
                return await RespondWithLlmAsync(context, history, userMessage, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                logger.LogWarning(ex, "LLM unavailable, using rule-based secretary");
            }
        }
        return RespondWithRules(context, userMessage);
    }

    // ---------- LLM path ----------
    private async Task<SecretaryResult> RespondWithLlmAsync(SecretaryContext c, IReadOnlyList<SecretaryTurn> history, string userMessage, CancellationToken ct)
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var sb = new StringBuilder();
        sb.AppendLine($"Tu es la secrétaire virtuelle de « {c.OrganizationName} » sur l'application TuniPlan (Tunisie).");
        if (!string.IsNullOrWhiteSpace(c.OrganizationDescription)) sb.AppendLine($"Activité : {c.OrganizationDescription}");
        sb.AppendLine($"Aujourd'hui : {c.TodayLocal.ToString("dddd d MMMM yyyy", fr)}.");
        sb.AppendLine("Réponds dans la langue du client : français, arabe, ou darija tunisienne (même écrite en lettres latines, ex. « nheb rdv ghodwa »).");
        sb.AppendLine("Règles strictes :");
        sb.AppendLine("- Tu ne confirmes JAMAIS un rendez-vous. Tu proposes au maximum 3 créneaux de la liste ci-dessous ; le client envoie une demande et le professionnel la confirme.");
        sb.AppendLine("- N'invente aucun créneau, prix ou service qui n'est pas dans les listes.");
        sb.AppendLine("- Pas de conseil médical ou juridique : oriente vers le rendez-vous.");
        sb.AppendLine("Services :");
        foreach (var s in c.Services) sb.AppendLine($"- id={s.Id} | {s.Name} | {s.DurationMinutes} min | {s.Price:0.###} DT");
        sb.AppendLine($"Service actuellement choisi : {c.SelectedService.Name} (id={c.SelectedService.Id}).");
        sb.AppendLine("Créneaux libres pour ce service (index | date et heure locales) :");
        foreach (var slot in c.AvailableSlots.Take(40)) sb.AppendLine($"- {slot.Index} | {slot.LocalStart.ToString("dddd d MMMM HH:mm", fr)}");
        sb.AppendLine("Réponds UNIQUEMENT avec un objet JSON : {\"reply\": \"texte court et chaleureux\", \"slot_indexes\": [entiers], \"service_id\": \"guid ou null\"}");

        var messages = history.TakeLast(10)
            .Select(t => new LlmMessage(t.FromUser ? "user" : "assistant", t.Content))
            .Append(new LlmMessage("user", userMessage))
            .ToList();

        var response = await llm.CompleteAsync(new LlmRequest(sb.ToString(), messages), ct);
        var json = ExtractJson(response.Text);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var reply = root.TryGetProperty("reply", out var r) ? r.GetString() ?? "" : response.Text;
        var indexes = new List<int>();
        if (root.TryGetProperty("slot_indexes", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var i in arr.EnumerateArray())
                if (i.TryGetInt32(out var idx) && c.AvailableSlots.Any(s => s.Index == idx)) indexes.Add(idx);
        Guid? serviceId = root.TryGetProperty("service_id", out var sid) && sid.ValueKind == JsonValueKind.String
                          && Guid.TryParse(sid.GetString(), out var g) && c.Services.Any(s => s.Id == g) ? g : null;
        return new SecretaryResult(reply, indexes.Take(3).ToList(), serviceId);
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new JsonException("No JSON object in LLM response.");
        return text[start..(end + 1)];
    }

    // ---------- Rule-based path (works without any API key) ----------
    private static SecretaryResult RespondWithRules(SecretaryContext c, string userMessage)
    {
        var text = Normalize(userMessage);
        var arabic = ArabicChars().IsMatch(userMessage);

        DateOnly? day = null;
        if (Has(text, "aujourd", "lyoum", "lyouma", "today", "اليوم")) day = c.TodayLocal;
        else if (Has(text, "apres-demain", "apres demain", "ba3d ghodwa")) day = c.TodayLocal.AddDays(2);
        else if (Has(text, "demain", "ghodwa", "ghodoua", "tomorrow", "غدوة", "غدا")) day = c.TodayLocal.AddDays(1);
        else
        {
            var names = new Dictionary<DayOfWeek, string[]>
            {
                [DayOfWeek.Monday] = ["lundi", "ethnin", "tnin", "monday", "الاثنين"],
                [DayOfWeek.Tuesday] = ["mardi", "thleth", "tuesday", "الثلاثاء"],
                [DayOfWeek.Wednesday] = ["mercredi", "erb3a", "arb3a", "wednesday", "الأربعاء"],
                [DayOfWeek.Thursday] = ["jeudi", "khmis", "thursday", "الخميس"],
                [DayOfWeek.Friday] = ["vendredi", "jem3a", "jom3a", "friday", "الجمعة"],
                [DayOfWeek.Saturday] = ["samedi", "sebt", "saturday", "السبت"],
                [DayOfWeek.Sunday] = ["dimanche", "a7ad", "ahad", "sunday", "الأحد"],
            };
            foreach (var (dow, words) in names)
                if (Has(text, words))
                {
                    var diff = ((int)dow - (int)c.TodayLocal.DayOfWeek + 7) % 7;
                    day = c.TodayLocal.AddDays(diff == 0 ? 7 : diff);
                    break;
                }
        }

        bool? morning = null;
        if (Has(text, "matin", "sbe7", "sbah", "morning", "الصباح", "صباح")) morning = true;
        else if (Has(text, "apres-midi", "apres midi", "3chiya", "aechiya", "l3chiya", "soir", "afternoon", "evening", "عشية", "المساء")) morning = false;

        var candidates = c.AvailableSlots.AsEnumerable();
        if (day is not null) candidates = candidates.Where(s => DateOnly.FromDateTime(s.LocalStart) == day);
        if (morning is not null) candidates = candidates.Where(s => (s.LocalStart.Hour < 12) == morning);
        var picked = candidates.Take(3).ToList();

        var fr = CultureInfo.GetCultureInfo("fr-FR");
        string reply;
        if (picked.Count == 0)
        {
            var next = c.AvailableSlots.Take(3).ToList();
            picked = next;
            reply = next.Count == 0
                ? (arabic ? "للأسف لا توجد مواعيد متاحة حاليًا. يمكنك الانضمام إلى قائمة الانتظار." : "Désolée, aucun créneau n'est disponible pour le moment. Vous pouvez rejoindre la liste d'attente.")
                : (arabic ? "لا يوجد موعد في هذا الوقت. هذه أقرب المواعيد المتاحة:" : "Pas de disponibilité à ce moment-là. Voici les créneaux les plus proches :");
        }
        else
        {
            reply = arabic
                ? $"بكل سرور! هذه المواعيد المتاحة لخدمة « {c.SelectedService.Name} »:"
                : $"Avec plaisir ! Voici des créneaux disponibles pour « {c.SelectedService.Name} » :";
        }

        if (picked.Count > 0)
        {
            reply += " " + string.Join(", ", picked.Select(s => s.LocalStart.ToString("dddd d MMMM 'à' HH:mm", fr))) + ".";
            reply += arabic
                ? " اختر موعدًا لإرسال طلبك، وسيؤكده المهني."
                : (c.ManualValidation
                    ? " Choisissez-en un pour envoyer votre demande : le professionnel la confirmera."
                    : " Choisissez-en un pour envoyer votre demande.");
        }
        return new SecretaryResult(reply, picked.Select(p => p.Index).ToList(), null);
    }

    private static bool Has(string text, params string[] words) => words.Any(w => text.Contains(Normalize(w), StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string input)
    {
        var d = input.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC).Replace('’', '\'');
    }

    [GeneratedRegex(@"\p{IsArabic}")]
    private static partial Regex ArabicChars();
}

public static class DependencyInjection
{
    public static IServiceCollection AddAiLayer(this IServiceCollection services)
    {
        services.AddScoped<ISecretaryAgent, SecretaryAgent>();
        services.AddScoped<IVoiceNoteAssistant, VoiceNoteAssistant>();
        return services;
    }
}
