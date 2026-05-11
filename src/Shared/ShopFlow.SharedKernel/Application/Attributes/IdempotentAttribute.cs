namespace ShopFlow.SharedKernel.Application.Attributes;

/// <summary>
/// Marker attribute the <c>MissingIdempotentAnalyzer</c> (ShopFlow0003)
/// looks for on webhook handler methods. Carrying this attribute is a
/// declaration that the handler persists the raw payload + the
/// <c>(channel_id, provider_event_id) UNIQUE</c> constraint per
/// AGENTS.md §6.36 before enqueuing for processing. Future cross-cutting
/// middleware can light up off this same marker.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class IdempotentAttribute : Attribute { }
