using System.Text;
using System.Text.Json;

namespace ShopFlow.Inventory.Infrastructure.Pagination;

/// <summary>
/// Opaque base64-JSON cursor for reservation ledger pagination
/// (Sprint-7.5 U6 / KTD4). Encodes the last row's <c>(occurredAt, id)</c>
/// so the handler can resume the DESC scan past that point via
/// Postgres row-value comparison.
/// </summary>
/// <remarks>
/// Why opaque base64: lets the server evolve the encoding (add a tenant
/// suffix, switch to ULID, etc.) without breaking client compatibility.
/// Clients hand the string back unchanged on the next request.
///
/// Encoding shape: URL-safe base64 of UTF-8 JSON
/// <c>{"occurredAt": "ISO-8601", "id": "GUID"}</c>. The
/// <see cref="OpaqueCursorPayload.Id"/> field is a GUID (matches
/// <c>reservations_ledger.id</c>); the <c>OccurredAt</c> is the same
/// timestamp the handler uses as its DESC order key
/// (<c>ConfirmedAt ?? ReleasedAt ?? ExpiredAt ?? CreatedAt</c>).
/// </remarks>
public static class OpaqueCursor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Encode a cursor payload to a URL-safe base64 string.
    /// </summary>
    public static string Encode(OpaqueCursorPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return Base64UrlEncode(json);
    }

    /// <summary>
    /// Decode an opaque cursor string. Returns <c>null</c> if the cursor is
    /// malformed (invalid base64, invalid JSON, missing required fields).
    /// Callers map a null return to a 400 with a stable error code rather
    /// than a 500.
    /// </summary>
    public static OpaqueCursorPayload? TryDecode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            return null;

        try
        {
            var bytes = Base64UrlDecode(cursor);
            var payload = JsonSerializer.Deserialize<OpaqueCursorPayload>(bytes, JsonOptions);
            if (payload is null || payload.Id == Guid.Empty)
                return null;
            return payload;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Base64UrlEncode(byte[] data)
    {
        var s = Convert.ToBase64String(data);
        return s.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string urlSafe)
    {
        var s = urlSafe.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2:
                s += "==";
                break;
            case 3:
                s += "=";
                break;
        }
        return Convert.FromBase64String(s);
    }
}

/// <summary>
/// Cursor payload — last row's (occurredAt, id). Postgres row-value
/// comparison <c>(occurred_at, id) &lt; (cursor.occurredAt, cursor.id)</c>
/// uses both fields to break ties when two ledger rows share an
/// <c>occurred_at</c> instant.
/// </summary>
public sealed record OpaqueCursorPayload(DateTime OccurredAt, Guid Id);
