using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CasinoShiz.MetaRestLoadTest;

internal sealed class LoadTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "MetaLoadTest";
    private const string Token = "meta-load-test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!string.Equals(Request.Headers.Authorization.FirstOrDefault(), $"Bearer {Token}", StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());

        var tenant = HeaderOrDefault("x-load-tenant", "meta-load-1");
        var scope = HeaderOrDefault("x-load-scope", "main");
        var user = HeaderOrDefault("x-load-user", "42");
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", user),
            new Claim("name", "Meta load test"),
            new Claim("tenant_id", tenant),
            new Claim("scope_id", scope),
        ],
        SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    private string HeaderOrDefault(string name, string fallback)
    {
        var value = Request.Headers[name].FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl)
            ? fallback
            : value;
    }
}
