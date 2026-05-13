namespace ShopFlow.Outbound.Application.Ports;

/// <summary>
/// Carrier-returned label + tracking number per Sprint-3-redux U6 plan
/// spec. Immutable record returned by <see cref="IMockShippingProvider.CreateLabelAsync"/>;
/// the controller persists both fields on the Order row before publishing
/// <c>TrackingPushedV1</c> + <c>ConfirmStockV1</c>.
/// </summary>
public sealed record ShippingLabel(string LabelUrl, string TrackingNumber);
