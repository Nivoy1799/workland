using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Worktown.Gateway.Api;
using Worktown.Gateway.Config;

namespace Worktown.Gateway.Auth;

/// <summary>
/// Endpoint filter: requires <c>Authorization: Bearer &lt;GATEWAY_SERVICE_TOKEN&gt;</c>.
/// Compares SHA-256 digests with a fixed-time compare so neither content nor length leaks.
/// Missing and wrong tokens get the same 401 body.
/// </summary>
public sealed class ServiceTokenFilter : IEndpointFilter
{
    private const string BearerPrefix = "Bearer ";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var options = http.RequestServices.GetRequiredService<IOptions<GatewayOptions>>().Value;
        if (!IsAuthorized(http.Request, options.ServiceToken))
            return Results.Json(ErrorResponse.For(ErrorResponse.Unauthorized), statusCode: StatusCodes.Status401Unauthorized);
        return await next(context).ConfigureAwait(false);
    }

    /// <summary>True when the request carries the configured bearer token. Also used by the rate limiter.</summary>
    public static bool IsAuthorized(HttpRequest request, string configuredToken)
    {
        var header = request.Headers.Authorization;
        if (header.Count != 1) return false;
        var value = header[0];
        // The auth scheme is case-insensitive (RFC 9110 section 11.1); the token itself is not.
        if (value is null || !value.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        return TokensMatch(value[BearerPrefix.Length..], configuredToken);
    }

    /// <summary>Fixed-time comparison of SHA-256(presented) and SHA-256(configured).</summary>
    public static bool TokensMatch(string presented, string configured)
    {
        if (configured.Length == 0) return false;
        Span<byte> a = stackalloc byte[32];
        Span<byte> b = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(presented), a);
        SHA256.HashData(Encoding.UTF8.GetBytes(configured), b);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
