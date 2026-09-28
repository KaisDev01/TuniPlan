using BL.Interfaces;
using DAL.Context;
using DAL.Interceptors;
using Entities;
using LoggerService;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NotificationService;

namespace Tests.Unit.Fakes;

public static class TestDb
{
    public static TuniPlanDbContext Create() =>
        new(new DbContextOptionsBuilder<TuniPlanDbContext>()
            .UseInMemoryDatabase($"tuniplan-tests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(new AuditSaveChangesInterceptor())
            .Options);
}

public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }
    public bool IsAuthenticated => UserId is not null;
    public HashSet<string> RolesSet { get; } = [];
    public bool IsInRole(string role) => RolesSet.Contains(role);
    public string? IpAddress => "127.0.0.1";
    public string? UserAgent => "tests";
    public Guid RequireUserId() => UserId ?? throw new Common.Exceptions.UnauthorizedException();
}

public sealed class FakeTokenService : ITokenService
{
    public (string Token, DateTime ExpiresAt) CreateAccessToken(User user) => ($"access-{user.Id}-{user.SecurityStamp}", DateTime.UtcNow.AddMinutes(15));
}

public sealed class FakeProtector : ISecretProtector
{
    public string Protect(string plaintext) => "p:" + plaintext;
    public string Unprotect(string protectedText) => protectedText[2..];
}

public sealed class FakeSessionCache : IUserSessionCache
{
    public List<Guid> Invalidated { get; } = [];
    public void Invalidate(Guid userId) => Invalidated.Add(userId);
}

public sealed class CapturingSms : ISmsSender
{
    public List<OutgoingMessage> Sent { get; } = [];
    public Task<bool> SendAsync(OutgoingMessage message, CancellationToken ct = default) { Sent.Add(message); return Task.FromResult(true); }
    public string LastCode() => System.Text.RegularExpressions.Regex.Match(Sent[^1].Body, "[0-9]{6}").Value;
}

public sealed class NullLoggerManager : ILoggerManager
{
    public void LogInfo(string message, params object?[] args) { }
    public void LogWarn(string message, params object?[] args) { }
    public void LogDebug(string message, params object?[] args) { }
    public void LogError(string message, params object?[] args) { }
    public void LogError(Exception exception, string message, params object?[] args) { }
    public void LogSecurity(string message, params object?[] args) { }
}

/// <summary>Accepts any token equal to a key of <see cref="Identities"/>.</summary>
public sealed class FakeExternalVerifier : IExternalIdentityVerifier
{
    public Dictionary<string, ExternalIdentity> Identities { get; } = new();
    public bool IsConfigured(Entities.Enums.ExternalProvider provider) => true;
    public Task<ExternalIdentity?> VerifyAsync(Entities.Enums.ExternalProvider provider, string token, CancellationToken ct = default) =>
        Task.FromResult(Identities.TryGetValue(token, out var id) && id.Provider == provider ? id : null);
}

public sealed class FakeClock : Common.Helpers.IClock
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
}

public sealed class CapturingPush : IPushSender
{
    public List<(IReadOnlyList<string> Tokens, PushMessage Message)> Sent { get; } = [];
    public HashSet<string> Unregistered { get; } = [];
    public Task<IReadOnlyList<string>> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken ct = default)
    {
        Sent.Add((tokens, message));
        return Task.FromResult<IReadOnlyList<string>>(tokens.Where(Unregistered.Contains).ToList());
    }
}

public sealed class NullWhatsApp : IWhatsAppSender
{
    public Task<bool> SendAsync(OutgoingMessage message, CancellationToken ct = default) => Task.FromResult(true);
}

public sealed class FakeFileStorage : OperationStorage.IFileStorage
{
    public List<string> Deleted { get; } = [];
    public Task<OperationStorage.StoredFile> SaveImageAsync(Stream content, string originalFileName, string contentType, string folder, CancellationToken ct = default)
    {
        var relative = $"{folder}/{Guid.NewGuid():N}.jpg";
        return Task.FromResult(new OperationStorage.StoredFile($"/uploads/{relative}", relative, content.Length, contentType));
    }
    public Task DeleteAsync(string url, CancellationToken ct = default) { Deleted.Add(url); return Task.CompletedTask; }
}
