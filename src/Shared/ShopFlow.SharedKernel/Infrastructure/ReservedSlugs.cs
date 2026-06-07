namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// Centralised tenant-slug deny list shared by
/// <see cref="TenantRoutingMiddleware"/> + the Auth.Api's in-controller
/// subdomain resolver + the shopflow-migrate <c>provision</c> command's
/// pre-create slug check (Sprint-8 U9 + U10).
/// </summary>
/// <remarks>
/// <para>Adding a slug here = rejecting it from new tenant provisioning
/// AND ensuring no existing routing path will resolve to a tenant of
/// that name. Sprint-8 ADV-001 mitigation: a tenant named "api" or
/// "www" would silently win the subdomain-to-host race against legitimate
/// infrastructure subdomains; pre-reserving them at the routing layer
/// closes the enumeration / impersonation surface.</para>
///
/// <para>The list is intentionally conservative — only operational
/// names that already serve infrastructure roles, plus a small set of
/// administrative + brand keywords. Adding a tenant-meaningful word
/// here is a feature-removing change; the inverse (allowing an
/// over-broad name through) is a security cost. Lean towards adding
/// when in doubt.</para>
/// </remarks>
public static class ReservedSlugs
{
    /// <summary>
    /// Lowercase entries — comparisons in callers always lowercase
    /// the candidate first. Sorted alphabetically for reviewability.
    /// </summary>
    public static readonly IReadOnlySet<string> Set = new HashSet<string>(StringComparer.Ordinal)
    {
        "admin",
        "api",
        "app",
        "auth",
        "billing",
        "cdn",
        "console",
        "dashboard",
        "dev",
        "docs",
        "help",
        "internal",
        "localhost",
        "mail",
        "manage",
        "ops",
        "public",
        "root",
        "shop",
        "shopflow",
        "staging",
        "static",
        "status",
        "support",
        "system",
        "www",
    };

    /// <summary>
    /// True when <paramref name="slug"/> is reserved (case-insensitive).
    /// Empty / null inputs return false — callers should validate
    /// non-emptiness separately (slug.required is a different error).
    /// </summary>
    public static bool IsReserved(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }
        return Set.Contains(slug.Trim().ToLowerInvariant());
    }
}
