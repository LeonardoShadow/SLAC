using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SLAC.Infrastructure.Security;

/// <summary>
/// Manejador de autenticación perimetral defensivo para SLAC.
/// Protege endpoints y hubs de SignalR verificando cookies de sesión
/// y conexiones internas seguras del servidor (SRS 4.7 / OWASP A01).
/// </summary>
public class SLACAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 1. Si ya existe identidad autenticada
        if (Context.User.Identity?.IsAuthenticated == true)
        {
            return AuthenticateResult.Success(new AuthenticationTicket(Context.User, Scheme.Name));
        }

        // 2. Intentar autenticar mediante el esquema de cookies si está presente
        var cookieResult = await Context.AuthenticateAsync("SLACCookies");
        if (cookieResult.Succeeded && cookieResult.Principal != null)
        {
            return AuthenticateResult.Success(new AuthenticationTicket(cookieResult.Principal, Scheme.Name));
        }

        // 3. Conexiones internas de circuito Blazor Server en loopback local
        var remoteIp = Context.Connection.RemoteIpAddress;
        if (remoteIp != null)
        {
            var ipToCheck = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;
            if (IPAddress.IsLoopback(ipToCheck))
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "internal-loopback-circuit"),
                    new Claim(ClaimTypes.Name, "Internal Classroom Projection"),
                    new Claim(ClaimTypes.Role, "Docente"),
                    new Claim("CircuitType", "ServerLoopback")
                };
                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
            }
        }

        // 4. Conexiones externas sin credenciales válidas quedan no autenticadas
        return AuthenticateResult.NoResult();
    }
}
