using System.Security.Claims;
using BL.Interfaces;
using DAO.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace TuniPlan.Security;

/// <summary>
/// Rejects access tokens whose security stamp no longer matches the user (password changed, logout from all devices,
/// account disabled/deleted). Stamps are cached for 2 minutes and the cache is invalidated on change.
/// </summary>
public sealed class SecurityStampValidator(IUnitOfWork uow, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    public async Task<bool> IsValidAsync(ClaimsPrincipal? principal, CancellationToken ct = default)
    {
        if (principal is null || !Guid.TryParse(principal.FindFirstValue("sub"), out var userId)) return false;
        var tokenStamp = principal.FindFirstValue(JwtTokenService.SecurityStampClaim);
        if (string.IsNullOrEmpty(tokenStamp)) return false;

        var stamp = await cache.GetOrCreateAsync(UserSessionCache.Key(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await uow.Users.QueryNoTracking()
                .Where(u => u.Id == userId && u.IsActive)
                .Select(u => u.SecurityStamp)
                .FirstOrDefaultAsync(ct);
        });
        return stamp is not null && stamp == tokenStamp;
    }
}

public sealed class UserSessionCache(IMemoryCache cache) : IUserSessionCache
{
    public static string Key(Guid userId) => $"sst:{userId}";
    public void Invalidate(Guid userId) => cache.Remove(Key(userId));
}
