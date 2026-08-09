using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CasinoShiz.FullRestWriteLoadTest;

internal sealed class LoadTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "FullRestWriteLoad";
    public const string Token = "full-rest-write-load-test";
    private const string LoadUserIdHeader = "X-Load-Test-User-Id";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!string.Equals(Request.Headers.Authorization, $"Bearer {Token}", StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());

        var userId = 8_000_001L;
        if (Request.Headers.TryGetValue(LoadUserIdHeader, out var requestedUserId)
            && (!long.TryParse(requestedUserId, NumberStyles.None, CultureInfo.InvariantCulture, out userId) || userId <= 0))
        {
            return Task.FromResult(AuthenticateResult.Fail($"{LoadUserIdHeader} must be a positive 64-bit integer."));
        }

        var identity = new ClaimsIdentity(
        [
            new Claim("sub", userId.ToString(CultureInfo.InvariantCulture)),
            new Claim("name", $"Full REST load user {userId}"),
            new Claim("tenant_id", "write-load"),
            new Claim("scope_id", "42"),
        ],
        SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
