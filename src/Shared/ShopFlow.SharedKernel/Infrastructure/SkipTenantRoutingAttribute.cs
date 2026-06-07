namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// Opt-out marker for <see cref="TenantRoutingMiddleware"/> per Sprint-4
/// plan U3. Endpoints carrying this attribute (controller class or action
/// method) skip the header / JWT / subdomain slug resolution and proceed
/// without an <c>IRequestContext</c> tenant binding.
/// </summary>
/// <remarks>
/// <para>The canonical use case is the webhook receiver: marketplaces post
/// to <c>/webhooks/{channelType}/{channelId}</c> with no tenant header
/// (they do not know the tenant — only their channel id). The receiver
/// resolves <c>channel_id → tenant_id</c> via <c>IChannelDirectory</c>
/// and binds <c>RequestContext</c> after the lookup + HMAC verification
/// succeed.</para>
/// <para>This is intentionally narrow. Endpoints exposed to the public
/// internet that depend on tenant context MUST NOT carry this attribute;
/// the middleware's default-deny posture is the load-bearing tenancy
/// correctness primitive.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SkipTenantRoutingAttribute : Attribute { }
