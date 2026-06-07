namespace ShopFlow.Mocks.Shopee.Endpoints;

/// <summary>
/// Sprint-5 U6 — mock surface for Shopee Open Platform v2's
/// <c>POST /api/v2/product/update_stock</c>. Accepts any JSON body, echoes
/// the <c>item_id</c> for trace, and returns 503 when
/// <see cref="ChaosState.IsStockUpdateChaosActive"/> is true.
/// </summary>
/// <remarks>
/// <para>Idempotency hint (<c>X-ShopFlow-Idempotency-Key</c>) is read into
/// the response so the harness can prove the adapter sent it; real Shopee
/// dedupes on item-id + version. Portfolio scope: accept-and-ack.</para>
/// </remarks>
public static class UpdateStockEndpoint
{
    public static IEndpointRouteBuilder MapShopeeUpdateStock(this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/v2/product/update_stock",
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

                long? itemId = null;
                try
                {
                    using var doc = await System.Text.Json.JsonDocument.ParseAsync(
                        ctx.Request.Body,
                        cancellationToken: ctx.RequestAborted
                    );
                    if (
                        doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("item_id", out var itemIdElement)
                        && itemIdElement.ValueKind == System.Text.Json.JsonValueKind.Number
                        && itemIdElement.TryGetInt64(out var parsed)
                    )
                    {
                        itemId = parsed;
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
                        item_id = itemId,
                        idempotency_key = idempotencyKey,
                    }
                );
            }
        );
        return app;
    }
}
