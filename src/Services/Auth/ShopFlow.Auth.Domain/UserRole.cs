namespace ShopFlow.Auth.Domain;

/// <summary>
/// Fixed role enum for Sprint-8 (KTD7). String-backed because the
/// per-tenant <c>users</c> table mirrors the value via a DB-level CHECK
/// constraint (<c>role IN ('Owner', 'Picker', 'Dispatcher', 'Packer')</c>);
/// the enum names and the SQL string values must agree exactly.
/// </summary>
/// <remarks>
/// Sprint-8 shipped 3 roles. Sprint-13 added <see cref="Packer"/> as the
/// 4th value (Sprint-13 K9 — appended at index 3 to preserve
/// <see cref="Owner"/>=0/<see cref="Picker"/>=1/<see cref="Dispatcher"/>=2
/// binary serialization ordering). Adding a 5th role requires (a) extending
/// this enum, (b) altering BOTH <c>chk_users_role</c> AND
/// <c>chk_role_permissions_role</c> CHECK constraints via a per-tenant
/// migration (Sprint-13 K2), and (c) updating the
/// <see cref="ShopFlow.Auth.Domain.Events.UserRoleChangedEvent"/> consumers.
/// </remarks>
public enum UserRole
{
    Owner,
    Picker,
    Dispatcher,
    Packer,
}
