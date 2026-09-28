using System.Security.Claims;
using System.Threading.RateLimiting;
using BL.Interfaces;
using BL.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using TuniPlan.Security;

namespace TuniPlan.Infrastructure;

public static class ServiceCollectionExtensions
{
    public const string AuthRateLimit = "auth";
    public const string AiRateLimit = "ai";
    public const string CorsPolicy = "front";

    public static IServiceCollection AddApiSecurity(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment env)
    {
        var jwt = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
            throw new InvalidOperationException(
                "Jwt:SigningKey must be at least 32 characters. Set it with 'dotnet user-secrets set Jwt:SigningKey <key>' or the Jwt__SigningKey environment variable.");

        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IUserSessionCache, UserSessionCache>();
        services.AddScoped<SecurityStampValidator>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        // Keys that encrypt TOTP secrets: persisted to disk so they survive restarts (use Azure Key Vault / Redis in a farm).
        var keysPath = configuration["DataProtection:KeysPath"] ?? Path.Combine(env.ContentRootPath, "keys");
        services.AddDataProtection().SetApplicationName("TuniPlan").PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = !env.IsDevelopment();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtTokenService.CreateKey(jwt.SigningKey),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                    RoleClaimType = "role"
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var validator = context.HttpContext.RequestServices.GetRequiredService<SecurityStampValidator>();
                        if (!await validator.IsValidAsync(context.Principal, context.HttpContext.RequestAborted))
                            context.Fail("Session revoked.");
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            // Secure by default: every endpoint requires a valid token unless it is marked [AllowAnonymous].
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsync(
                    "{\"status\":429,\"title\":\"Trop de requêtes. Réessayez dans un instant.\",\"code\":\"rate_limited\"}", token);
            };

            // Login / register / codes: per IP (brute force is also stopped by account lockout and code attempt limits)
            var authPermitLimit = configuration.GetValue("RateLimits:AuthPerMinute", 30);
            options.AddPolicy(AuthRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            // AI secretary: 20 messages per minute per user
            options.AddPolicy(AiRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.FindFirstValue("sub") ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            // Everything else: 300 requests per minute per user (or IP)
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.FindFirstValue("sub") ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
        {
            // "*" = any website may call the API (safe here: auth uses Bearer tokens, not cookies)
            if (origins.Contains("*")) p.AllowAnyOrigin();
            // Patterns such as "https://tuniplan-*.vercel.app" allow Vercel preview deployments
            else p.SetIsOriginAllowed(origin => IsOriginAllowed(origin, origins));
            p.WithMethods("GET", "POST", "PUT", "DELETE")
             .WithHeaders("Authorization", "Content-Type", "Accept-Language")
             .SetPreflightMaxAge(TimeSpan.FromHours(1));
        }));

        return services;
    }

    /// <summary>Exact match, or a pattern where "*" stands for one or more characters of a single host label (no dot, no slash).</summary>
    internal static bool IsOriginAllowed(string origin, IEnumerable<string> allowed)
    {
        var candidate = origin.TrimEnd('/');
        foreach (var entry in allowed)
        {
            var pattern = entry.Trim().TrimEnd('/');
            if (pattern.Length == 0) continue;
            if (!pattern.Contains('*'))
            {
                if (string.Equals(pattern, candidate, StringComparison.OrdinalIgnoreCase)) return true;
                continue;
            }
            var regex = "^" + string.Join("[a-z0-9-]+", pattern.Split('*').Select(System.Text.RegularExpressions.Regex.Escape)) + "$";
            if (System.Text.RegularExpressions.Regex.IsMatch(candidate, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }
}
