namespace ShopFlow.Auth.Application.Dtos;

/// <summary>
/// Wire DTOs for the Owner-only admin surfaces (Sprint-8 U8 ships the
/// handler impls). The admin flow creates additional users in the
/// current tenant + manages their roles + resets their passwords. All
/// endpoints require <c>role=Owner</c> in the access token; the
/// authorization filter rejects non-Owner callers with 403 before the
/// handler runs.
/// </summary>
public sealed record CreateUserRequest(string Email, string Role);

/// <summary>
/// Response from <c>POST /api/auth/admin/users</c>. The server
/// generates a strong temporary password (16 URL-safe chars from
/// the U8 <c>PasswordGenerator</c>) and returns it ONCE in plaintext
/// for the admin to relay to the new user. The plaintext is NEVER
/// stored, logged, or echoed in subsequent calls — the OTel
/// instrumentation filters this field out of response-body capture
/// (KTD9 — response-body redaction for <c>temporary_password</c>).
/// </summary>
public sealed record CreateUserResponse(
    Guid UserId,
    string Email,
    string Role,
    string TemporaryPassword);

/// <summary>
/// Discriminated request body for <c>PATCH /api/auth/admin/users/{id}</c>
/// (KTD8 consolidation — single endpoint, operation-tag dispatch). The
/// admin handler reads <see cref="Operation"/> and routes to one of
/// three branches: <c>set_role</c> requires <see cref="NewRole"/>;
/// <c>reset_password</c> ignores both other fields and returns a new
/// temporary password; <c>deactivate</c> ignores both other fields and
/// flips <c>IsActive=false</c> + revokes the user's refresh tokens.
/// </summary>
public sealed record UpdateUserRequest(string Operation, string? NewRole);

/// <summary>
/// Response from the <c>reset_password</c> branch of
/// <see cref="UpdateUserRequest"/>. Carries the freshly-generated
/// temporary password (same one-time-display + redaction discipline as
/// <see cref="CreateUserResponse"/>).
/// </summary>
public sealed record ResetPasswordResponse(Guid UserId, string TemporaryPassword);

/// <summary>
/// Row shape in <see cref="ListUsersResponse"/>. The plaintext password
/// + PHC hash never appear in admin listings — only metadata.
/// <see cref="LastLoginAt"/> is null for users who have never
/// successfully authenticated.
/// </summary>
public sealed record UserSummary(
    Guid Id,
    string Email,
    string Role,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt);

public sealed record ListUsersResponse(IReadOnlyList<UserSummary> Users, int Page, int PageSize);
