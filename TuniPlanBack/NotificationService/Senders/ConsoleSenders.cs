using Microsoft.Extensions.Logging;

namespace NotificationService.Senders;

/// <summary>Development senders: nothing leaves the server, messages are written to the log.</summary>
public sealed class ConsoleSmsSender(ILogger<ConsoleSmsSender> logger) : ISmsSender
{
    public Task<bool> SendAsync(OutgoingMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("[SMS → {To}] {Body}", message.To, message.Body);
        return Task.FromResult(true);
    }
}

public sealed class ConsoleWhatsAppSender(ILogger<ConsoleWhatsAppSender> logger) : IWhatsAppSender
{
    public Task<bool> SendAsync(OutgoingMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("[WhatsApp → {To}] {Body}", message.To, message.Body);
        return Task.FromResult(true);
    }
}

public sealed class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task<bool> SendAsync(OutgoingMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("[Email → {To}] {Subject}: {Body}", message.To, message.Subject, message.Body);
        return Task.FromResult(true);
    }
}

public sealed class ConsolePushSender(ILogger<ConsolePushSender> logger) : IPushSender
{
    public Task<bool> SendAsync(Guid userId, string title, string body, CancellationToken ct = default)
    {
        logger.LogInformation("[Push → {UserId}] {Title}: {Body}", userId, title, body);
        return Task.FromResult(true);
    }
}
