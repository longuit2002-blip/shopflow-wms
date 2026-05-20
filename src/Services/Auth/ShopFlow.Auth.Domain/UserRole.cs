namespace ShopFlow.Auth.Domain;

/// <summary>
/// Fixed role enum for Sprint-8 (KTD7). String-backed because the
/// per-tenant <c>users</c> table mirrors the value via a DB-level CHECK
/// constraint (<c>role IN ('Owner', 'Picker', 'Dispatcher')</c>); the
/// enum names and the SQL string values must agree exactly.
/// </summary>
/// <remarks>
/// Sprint-8 ships 3 roles. Adding a 4th in Sprint-9+ requires (a) extending
/// this enum, (b) altering the CHECK constraint via a per-tenant migration,
/// and (c) updating the <see cref="ShopFlow.Auth.Domain.Events.UserRoleChangedEvent"/>
/// consumers. YAGNI on a role-permissions table per KTD7 — revisit when
/// a 5th role lands or an RBAC matrix becomes real.
/// </remarks>
public enum UserRole
{
    Owner,
    Picker,
    Dispatcher,
}
