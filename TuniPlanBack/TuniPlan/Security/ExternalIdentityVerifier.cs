using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BL.Interfaces;
using Entities.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace TuniPlan.Security;

public sealed class ExternalAuthOptions
{
    public const string Section = "ExternalAuth";
    public GoogleOptions Google { get; set; } = new();
    public FacebookOptions Facebook { get; set; } = new();

    public sealed class GoogleOptions
    {
        /// <summary>OAuth client IDs allowed as token audience (web, iOS and Android clients of the same Google project).</summary>
        public string[] ClientIds { get; set; } = [];
    }

    public sealed class FacebookOptions
    {
        public string? AppId { get; set; }
        /// <summary>Secret: environment variable ExternalAuth__Facebook__AppSecret, never in git.</summary>
        public string? AppSecret { get; set; }
        public string GraphUrl { get; set; } = "https://graph.facebook.com/v19.0";
    }
}

/// <summary>
/// Google: the ID token signature is checked with Google's public keys (cached), plus issuer, audience and lifetime.
/// Facebook: the access token is checked with /debug_token (must be issued for our app), then /me gives the profile.
/// </summary>
public sealed class ExternalIdentityVerifier(HttpClient http, IOptions<ExternalAuthOptions> options, ILogger<ExternalIdentityVerifier> logger)
    : IExternalIdentityVerifier
{
    private static readonly ConfigurationManager<OpenIdConnectConfiguration> GoogleConfiguration = new(
        "https://accounts.google.com/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever { RequireHttps = true });

    private readonly JsonWebTokenHandler _jwtHandler = new();

    public bool IsConfigured(ExternalProvider provider) => provider switch
    {
        ExternalProvider.Google => options.Value.Google.ClientIds.Any(id => !string.IsNullOrWhiteSpace(id)),
        ExternalProvider.Facebook => !string.IsNullOrWhiteSpace(options.Value.Facebook.AppId) && !string.IsNullOrWhiteSpace(options.Value.Facebook.AppSecret),
        _ => false
    };

    public Task<ExternalIdentity?> VerifyAsync(ExternalProvider provider, string token, CancellationToken ct = default) => provider switch
    {
        ExternalProvider.Google => VerifyGoogleAsync(token, ct),
        ExternalProvider.Facebook => VerifyFacebookAsync(token, ct),
        _ => Task.FromResult<ExternalIdentity?>(null)
    };

    private async Task<ExternalIdentity?> VerifyGoogleAsync(string idToken, CancellationToken ct)
    {
        var config = await GoogleConfiguration.GetConfigurationAsync(ct);
        var result = await _jwtHandler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuers = ["accounts.google.com", "https://accounts.google.com"],
            ValidAudiences = options.Value.Google.ClientIds,
            IssuerSigningKeys = config.SigningKeys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        });
        if (!result.IsValid)
        {
            logger.LogInformation("Google token rejected: {Error}", result.Exception?.Message);
            return null;
        }

        string? Claim(string name) => result.Claims.TryGetValue(name, out var v) ? v?.ToString() : null;
        var subject = Claim("sub");
        if (string.IsNullOrEmpty(subject)) return null;
        var emailVerified = string.Equals(Claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        return new ExternalIdentity(ExternalProvider.Google, subject, Claim("email"), emailVerified, Claim("given_name"), Claim("family_name"));
    }

    private async Task<ExternalIdentity?> VerifyFacebookAsync(string accessToken, CancellationToken ct)
    {
        var fb = options.Value.Facebook;
        var graph = fb.GraphUrl.TrimEnd('/');
        var appToken = Uri.EscapeDataString($"{fb.AppId}|{fb.AppSecret}");
        var input = Uri.EscapeDataString(accessToken);

        var debug = await GetAsync<FacebookDebugResponse>($"{graph}/debug_token?input_token={input}&access_token={appToken}", ct);
        var data = debug?.Data;
        if (data is null || !data.IsValid || data.AppId != fb.AppId || string.IsNullOrEmpty(data.UserId))
        {
            logger.LogInformation("Facebook token rejected (valid={Valid}, app={App})", data?.IsValid, data?.AppId);
            return null;
        }

        var me = await GetAsync<FacebookProfile>($"{graph}/me?fields=id,first_name,last_name,email&access_token={input}", ct);
        if (me is null || me.Id != data.UserId) return null;
        // Facebook only returns an email the user has confirmed
        return new ExternalIdentity(ExternalProvider.Facebook, me.Id, me.Email, me.Email is not null, me.FirstName, me.LastName);
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct) where T : class
    {
        using var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogInformation("Facebook Graph call failed: HTTP {Status}", (int)response.StatusCode);
            return null;
        }
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private sealed class FacebookDebugResponse
    {
        [JsonPropertyName("data")] public FacebookDebugData? Data { get; set; }
    }

    private sealed class FacebookDebugData
    {
        [JsonPropertyName("is_valid")] public bool IsValid { get; set; }
        [JsonPropertyName("app_id")] public string? AppId { get; set; }
        [JsonPropertyName("user_id")] public string? UserId { get; set; }
    }

    private sealed class FacebookProfile
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("first_name")] public string? FirstName { get; set; }
        [JsonPropertyName("last_name")] public string? LastName { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
    }
}
