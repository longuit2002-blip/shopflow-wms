namespace ShopFlow.Mocks.Lazada.Endpoints;

/// <summary>
/// Finish-line U7 — mock surface for the Lazada
/// <c>POST /api/v3/product/update_stock</c> endpoint. Accepts any JSON
/// body, echoes the <c>seller_sku</c> for trace, and returns 503 when
/// <see cref="ChaosState.IsStockUpdateChaosActive"/> is true. Mirrors the
/// Shopee mock's <c>UpdateStockEndpoint</c>.
/// </summary>
/// <remarks>
/// <para>Idempotency hint (<c>X-ShopFlow-Idempotency-Key</c>) is read into
/// the response so the harness can prove the adapter sent it; real Lazada
/// dedupes upstream. Portfolio scope: accept-and-ack.</para>
/// </remarks>
public static class UpdateStockEndpoint
{
    public static IEndpointRouteBuilder MapLazadaUpdateStock(this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/v3/product/update_stock",
            async (HttpContext ctx, ChaosState chaos) =>
            {
                if (chaos.IsStockUpdateChaosActive)
                {
                    return Results.StatusCode(503);
                }

                var idempotencyKey = ctx.Request.Headers.TryGetValue(
                    "X-ShopFlow-Idempotency-Key",
                    out var raw
                )
                    ? raw.ToString()
                    : null;

                string? sellerSku = null;
                try
                {
                    using var doc = await System.Text.Json.JsonDocument.ParseAsync(
                        ctx.Request.Body,
                        cancellationToken: ctx.RequestAborted
                    );
                    if (
                        doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("seller_sku", out var sellerSkuElement)
                        && sellerSkuElement.ValueKind == System.Text.Json.JsonValueKind.String
                    )
                    {
                        sellerSku = sellerSkuElement.GetString();
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    return Results.BadRequest(new { error = "invalid JSON body" });
                }

                return Results.Ok(
                    new
                    {
                        message = "stock_updated",
                        seller_sku = sellerSku,
                        idempotency_key = idempotencyKey,
                    }
                );
            }
        );
        return app;
    }
}
