---
title: "perm claim must be a JSON array, not a space-delimited string"
date: 2026-05-20
type: convention
sprint: 9
units: [U6, U7]
---

## Rule

Emit the JWT `perm` claim as a JSON string array (one `Claim("perm", value)` per permission key). Do NOT collapse the permissions into a single space-delimited string under a single claim.

## Why

ASP.NET Core's authorization policy matcher `RequireClaim("perm", <key>)` does **exact value equality** against the claim value. With a single space-delimited claim like `"inventory.read inventory.adjust auth.admin.users.list"`, the matcher would never see `"inventory.read"` as a discrete value — every policy check would fail with 403 for legitimate users.

`Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler` automatically flattens N claims with the same type into a single JSON array under that claim name on the wire. On the validator side it deserializes the array back into N separate `Claim` objects of that type. `RequireClaim` then matches element-by-element.

## How to apply

In any JWT issuer that emits permission grants:

```csharp
var claims = new List<Claim>
{
    new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
    new(JwtRegisteredClaimNames.Email, user.Email),
    new("role", user.Role.ToString()),
    new("tenant_slug", tenantSlug),
};

foreach (var perm in rolePermissions)
{
    claims.Add(new Claim("perm", perm));  // ← one Claim per key
}
```

NOT:

```csharp
claims.Add(new Claim("perm", string.Join(" ", rolePermissions)));  // ← broken
```

## Where it lives

- Emitted in `src/Services/Auth/ShopFlow.Auth.Infrastructure/Tokens/JwtTokenIssuer.cs`.
- Pinned in `tests/ShopFlow.Auth.UnitTests/Tokens/JwtTokenIssuerTests.cs#IssueAccessToken_PermClaim_EmittedAsJsonArray`.
- Consumed by `src/Shared/ShopFlow.SharedKernel/Authorization/PermissionPolicyExtensions.cs` policy registration loop.

## Reviewers' checklist

- Search for `new Claim("perm"` — every occurrence should be inside a loop, never a join.
- New JWT issuer impls (Sprint-10+ key rotation refactor, etc.) follow the same shape.
