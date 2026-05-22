using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ShopFlow.SharedKernel.Authorization;

namespace ShopFlow.SharedKernel.Infrastructure.SignalR;

/// <summary>
/// Sprint-7 plan U5 — single shared SignalR hub for tenant-scoped server →
/// client fan-out. Every connected client joins exactly one group named
/// <c>"tenant:{slug}"</c>; relay consumers (U6 <c>StockChangedRelayConsumer</c>
/// and <c>SagaTransitionedRelayConsumer</c>) reach into the hub via
/// <see cref="IHubContext{TenantHub}"/> and send to that group.
/// </summary>
/// <remarks>
/// <para>The hub class is intentionally minimal — there are no
/// <c>[HubMethodName]</c>-marked methods because Sprint-7 ships a read-only
/// push surface. Group joins happen in <see cref="TenantBindingHubFilter"/>
/// on <see cref="Hub.OnConnectedAsync"/>; clients do not invoke server
/// methods to subscribe. The class exists primarily as a typed marker so
/// MapHub can produce a SignalR endpoint and so relays can resolve the
/// hub context for sends.</para>
///
/// <para>Auth posture: Sprint-10.5 U3 + KTD4 — <see cref="AuthorizeAttribute"/>
/// at class level pins the <see cref="PermissionKeys.HubConnect"/> policy so
/// the hub joins the per-permission gating cross-cut Sprint-10 attached to
/// the 33 controller actions. The policy registered by
/// <c>AddShopFlowPermissionPolicies</c> inherits
/// <c>RequireAuthenticatedUser()</c> alongside
/// <c>RequireClaim("perm", "hub.connect")</c>, so every connection must carry
/// both a validated JWT AND the <c>hub.connect</c> perm (the access-token
/// query parameter is converted to <c>context.Token</c> inside the JwtBearer
/// <c>OnMessageReceived</c> handler in <c>AddShopFlowDefaults</c>; URLs
/// containing <c>?access_token=</c> are also scrubbed before they reach
/// request logging per doc-review SEC-001).</para>
///
/// <para>Tenancy posture: <see cref="SkipTenantRoutingAttribute"/> at
/// class level keeps <c>TenantRoutingMiddleware</c> from rejecting the
/// negotiation / connection requests as missing tenant context. The hub
/// reads <c>tenant_slug</c> directly from the JWT claim inside
/// <see cref="TenantBindingHubFilter.OnConnectedAsync"/> after auth runs,
/// then binds <see cref="Application.RequestContext"/> via the K12 / KTD7
/// singleton-scope-binding pattern.</para>
/// </remarks>
[Authorize(Policy = PermissionKeys.HubConnect)]
[SkipTenantRouting]
public sealed class TenantHub : Hub { }
