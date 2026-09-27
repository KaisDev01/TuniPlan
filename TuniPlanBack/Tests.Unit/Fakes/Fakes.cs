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
