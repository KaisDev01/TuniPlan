namespace NotificationService;

public sealed record OutgoingMessage(string To, string Body, string? Subject = null);

public interface ISmsSender
{
    Task<bool> SendAsync(OutgoingMessage message, CancellationToken ct = default);
}

public interface IWhatsAppSender
{
    Task<bool> SendAsync(OutgoingMessage message, CancellationToken ct = default);
}

public interface IEmailSender
{
    Task<bool> SendAsync(OutgoingMessage message, CancellationToken ct = default);
}

public sealed record PushMessage(string Title, string Body, IReadOnlyDictionary<string, string>? Data = null);

public interface IPushSender
{
    /// <summary>Sends one notification to the given device tokens (Expo push tokens).</summary>
    /// <returns>Tokens the provider reported as no longer valid (app uninstalled...): delete them.</returns>
    Task<IReadOnlyList<string>> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken ct = default);
}

public sealed class NotificationOptions
{
    public const string Section = "Notifications";
    /// <summary>"Console" (development: messages are only logged) or "Http" (real provider).</summary>
    public string SmsProvider { get; set; } = "Console";
    public string? SmsApiUrl { get; set; }
    public string? SmsApiKey { get; set; }
    public string SmsSenderName { get; set; } = "TuniPlan";
    public string? WhatsAppApiUrl { get; set; }
    public string? WhatsAppToken { get; set; }
    /// <summary>"Console" (development: pushes are only logged) or "Expo" (Expo push service).</summary>
    public string PushProvider { get; set; } = "Console";
    public string ExpoPushUrl { get; set; } = "https://exp.host/--/api/v2/push/send";
    /// <summary>Optional: only when "Enhanced push security" is enabled in the Expo project.</summary>
    public string? ExpoAccessToken { get; set; }
}
