using System.Collections.Concurrent;
using System.Reflection;

namespace ShopFlow.Notification.Infrastructure.Templates;

/// <summary>
/// Loads <c>.tmpl</c> files shipped as <c>EmbeddedResource</c> next to
/// the Infrastructure assembly. Resource name convention is
/// <c>ShopFlow.Notification.Infrastructure.Templates.&lt;kind&gt;.{txt|html}.tmpl</c>
/// — MSBuild rewrites slashes to dots when embedding. Loaded contents
/// are cached per assembly lifetime (templates are immutable embedded
/// resources, no reload).
/// </summary>
public sealed class TemplateResourceLoader
{
    private static readonly Assembly InfrastructureAssembly =
        typeof(TemplateResourceLoader).Assembly;

    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <summary>
    /// Load the template body for a given <paramref name="resourceKey"/>
    /// such as <c>password-reset.txt</c> or <c>account-locked.html</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The resource is missing from the embedded set — usually a
    /// .csproj <c>EmbeddedResource</c> glob mismatch.
    /// </exception>
    public string Load(string resourceKey)
    {
        ArgumentNullException.ThrowIfNull(resourceKey);

        return Cache.GetOrAdd(resourceKey, LoadFromAssembly);
    }

    private static string LoadFromAssembly(string resourceKey)
    {
        var manifestName =
            $"ShopFlow.Notification.Infrastructure.Templates.{resourceKey}.tmpl";

        using var stream = InfrastructureAssembly.GetManifestResourceStream(manifestName);
        if (stream is null)
        {
            var available = string.Join(
                ", ",
                InfrastructureAssembly.GetManifestResourceNames()
            );
            throw new InvalidOperationException(
                $"Notification template '{manifestName}' not found in embedded resources. "
                    + $"Available: {available}"
            );
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
