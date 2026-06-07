---
title: "TOTP KEK rotation via per-row key id with lazy read fallback"
date: 2026-05-20
type: architecture
sprint: 9
units: [U3, U4]
---

## Rule

Each `user_totp_secrets` row stores its encrypted secret blob alongside a `totp_key_id smallint NOT NULL` field. The cipher's `Encrypt` always uses the **Current** KEK + stamps the row with `CurrentKeyId`. `Decrypt` selects between Current and Previous based on the row's key id. Rotation is **lazy**: a background sweep (Sprint-10+ ops work) drains Previous-encrypted rows to Current; the active read path falls back transparently.

## Why

OWASP Cryptographic Storage Cheat Sheet recommends two slots (Current + Previous) so KEK rotation doesn't require a synchronous re-encrypt of every encrypted row. The two-slot pattern enables:

1. **Zero-downtime rotation**: bump `Auth:TotpKek:Current`, set `Auth:TotpKek:Previous = <old Current>`, deploy. New encrypts use the new KEK; old reads continue to work.
2. **Operational decoupling**: the heavy work (re-encrypt sweep) runs as a background job, not blocking the rotation deploy.
3. **Per-row durability**: a row encrypted under any historical KEK can still be decrypted as long as that KEK is in either slot — operators don't need to coordinate a full re-encrypt before the rotation deploy completes.

## How to apply

Cipher impl:

```csharp
public byte[]? Decrypt(byte[] blob, int keyId, Guid tenantId, Guid userId)
{
    byte[] key;
    if (keyId == _currentKeyId) key = _currentKey;
    else if (_previousKey is not null) key = _previousKey;
    else return null;  // unrecoverable — Sprint-10+ should never hit this

    // AES-GCM decrypt with AAD = tenant_id || user_id
}
```

`Encrypt` always returns `(blob, CurrentKeyId)` so the row stamp matches.

## Where it lives

- `src/Services/Auth/ShopFlow.Auth.Infrastructure/Mfa/AesTotpSecretCipher.cs`.
- `src/Services/Auth/ShopFlow.Auth.Infrastructure/Mfa/TotpKekOptions.cs` (`Current`, `Previous`, `CurrentKeyId`).
- Schema: `user_totp_secrets.totp_key_id` smallint, `EncryptedSecret` bytea blob in framed `[nonce(12)][cipher(N)][tag(16)]` shape.
- Pinned: `tests/ShopFlow.Auth.UnitTests/Mfa/AesTotpSecretCipherTests.cs#Decrypt_WithPreviousKeyId_FallsBackThroughRotationSlot`.

## Operational pre-flight

Before first prod deploy:

1. `openssl rand -base64 32` → `Auth:TotpKek:Current` (replace dev sentinel).
2. Leave `Previous` empty initially.
3. On rotation: regenerate Current, move old Current into Previous, deploy, optionally run sweep.

## Reviewers' checklist

- Per-row AAD `tenant_id || user_id` ties ciphertext to the row context — ciphertext lifted to a different `(tenant, user)` will throw `AuthenticationTagMismatchException`.
- `CurrentKeyId` must be > 0 (smallint range); plan for `short` overflow at ~32k rotations (~876 years at one rotation/decade — not a practical concern).
