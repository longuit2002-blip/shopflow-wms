using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace ShopFlow.SharedKernel.Authorization;

/// <summary>
/// Sprint-9 U7 — registers one ASP.NET Core authorization policy per
/// key in <see cref="PermissionKeys.All"/>. The policies match against
/// the JSON-array <c>perm</c> claim emitted by U6's JwtTokenIssuer
/// (KTD1 + KTD4).
/// </summary>
/// <remarks>
/// Each policy carries <c>RequireAuthenticatedUser</c> +
/// <c>RequireClaim("perm", &lt;key&gt;)</c>. ASP.NET resolves the
/// claim values from the validated JWT's flattened claim list; a
/// JSON array <c>"perm": ["a", "b"]</c> on the wire surfaces as N
/// separate claims of type <c>perm</c> sharing the type name.
/// <c>RequireClaim("perm", "a")</c> matches the first claim whose
/// value equals <c>"a"</c>.
/// </remarks>
public static class PermissionPolicyExtensions
{
    /// <summary>
    /// Register one policy per <see cref="PermissionKeys.All"/> entry.
    /// Idempotent — multiple invocations override the previous
    /// registration with the same name (last-write-wins via
    /// <c>AddAuthorizationBuilder().AddPolicy</c>).
    /// </summary>
    public static IServiceCollection AddShopFlowPermissionPolicies(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = services.AddAuthorizationBuilder();
        foreach (var key in PermissionKeys.All)
        {
            builder.AddPolicy(key, p =>
                p.RequireAuthenticatedUser().RequireClaim("perm", key));
        }
        return services;
    }
}
