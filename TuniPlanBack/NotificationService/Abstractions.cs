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

public interface IPushSender
{
    /// <summary>Sends a push notification to all devices of the user (Expo push, FCM…).</summary>
    Task<bool> SendAsync(Guid userId, string title, string body, CancellationToken ct = default);
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
}
