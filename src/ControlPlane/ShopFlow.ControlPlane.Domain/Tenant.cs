using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.ControlPlane.Domain;

/// <summary>
/// Control-plane aggregate root representing one tenant of the WMS. Per
/// ADR-0003 the tenant is the database boundary: every business table lives
/// inside <c>shopflow_t_&lt;slug&gt;</c>, and this aggregate carries the
/// lifecycle state machine that <c>shopflow-migrate provision|archive</c>
/// (U6) drives. Persisted in <c>shopflow_control.tenants</c> per Tech Design
/// v3.0 §1.5.
/// </summary>
/// <remarks>
/// <para>Lifecycle invariants (see <see cref="TenantStatus"/>):</para>
/// <list type="bullet">
///   <item><description><c>Pending</c> → <c>Provisioning</c> via <see cref="BeginProvisioning"/>.</description></item>
///   <item><description><c>Provisioning</c> → <c>Ready</c> via <see cref="MarkProvisioned"/>; stamps <see cref="ProvisionedAt"/>.</description></item>
///   <item><description><c>Provisioning</c> → <c>ProvisioningFailed</c> via <see cref="MarkProvisioningFailed"/> with a reason.</description></item>
///   <item><description><c>ProvisioningFailed</c> → <c>Provisioning</c> on retry (idempotent re-provision per plan U6).</description></item>
///   <item><description><c>Ready</c> → <c>Archiving</c> via <see cref="BeginArchiving"/>; stamps <see cref="ArchivingAt"/>.</description></item>
///   <item><description><c>Archiving</c> → <c>Archived</c> via <see cref="CompleteArchiving"/>; stamps <see cref="ArchivedAt"/>. The actual <c>DROP DATABASE</c> is a deferred Phase-2 cron job per plan U6.</description></item>
/// </list>
/// <para>Invalid transitions return <see cref="Result.Failure"/> rather than
/// throwing — they are expected outcomes (re-provision races, archive of an
/// already-archived tenant). AGENTS.md §4.24.</para>
///
/// <para>Inherits <see cref="BaseEntity"/> rather than <see cref="AggregateRoot"/>
/// because the inherited <c>byte[] RowVersion</c> on <see cref="AggregateRoot"/>
/// is incompatible with the Postgres <c>xid</c> column type the migration
/// ships (<c>row_version xid NOT NULL DEFAULT (txid_current())::text::xid</c>).
/// <see cref="Tenant"/> declares its own <see cref="uint"/> RowVersion to
/// match. Same deviation as <c>StockItem</c> (Phase-0-redux U8); the inherited
/// domain-event buffer + Created/Updated stamps from <see cref="BaseEntity"/>
/// survive.</para>
/// </remarks>
public sealed class Tenant : BaseEntity
{
    /// <summary>
    /// Postgres <c>xid</c> row-version token for optimistic concurrency.
    /// EF treats this as the concurrency token; conflicts surface as
    /// <c>DbUpdateConcurrencyException</c>. See entity configuration for the
    /// column type and default mapping.
    /// </summary>
    public uint RowVersion { get; private set; }


    /// <summary>
    /// URL-safe short identifier, unique per cluster. Drives PgBouncer pool
    /// keys, subdomain routing, and the <c>shopflow_t_&lt;slug&gt;</c>
    /// database name.
    /// </summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>
    /// Postgres database name (typically <c>shopflow_t_&lt;slug&gt;</c>).
    /// Unique per cluster. Materialised at <see cref="Create"/> time so the
    /// provisioning workflow does not have to re-derive it from the slug.
    /// </summary>
    public string DbName { get; private set; } = string.Empty;

    /// <summary>Logical region (Phase-3+ residency hint).</summary>
    public string Region { get; private set; } = string.Empty;

    /// <summary>Pricing tier (<c>free</c>, <c>paid</c>, <c>enterprise</c>).</summary>
    public string Tier { get; private set; } = string.Empty;

    public TenantStatus Status { get; private set; } = TenantStatus.Pending;

    /// <summary>
    /// Business registration identifier (PDPA SEA compliance — the legal
    /// entity that owns the data). Optional at <see cref="Create"/> time;
    /// required before <see cref="MarkProvisioned"/> for enterprise tenants
    /// (enforced by the provisioning workflow, not here).
    /// </summary>
    public string? BusinessRegistration { get; private set; }

    /// <summary>
    /// JSON array of sub-processors disclosed for PDPA SEA Article 21.
    /// Stored as <c>JSONB</c> in Postgres; persisted as a raw JSON string at
    /// the domain level to keep the layer free of System.Text.Json deps.
    /// </summary>
    public string SubProcessorsJson { get; private set; } = "[]";

    public DateTime? ProvisionedAt { get; private set; }

    public DateTime? ArchivingAt { get; private set; }

    public DateTime? ArchivedAt { get; private set; }

    /// <summary>
    /// Set when PDPA SEA Article 49 breach notification has been sent to the
    /// regulator. Phase-0-redux does not implement the workflow; the column
    /// exists so the catalog schema matches Tech Design v3.0 §1.5 verbatim.
    /// </summary>
    public DateTime? BreachNotifiedAt { get; private set; }

    /// <summary>Last failure reason (only populated in <c>ProvisioningFailed</c>).</summary>
    public string? LastFailureReason { get; private set; }

    private Tenant() { }

    public static Result<Tenant> Create(
        string slug,
        string dbName,
        string region,
        string tier,
        string? businessRegistration = null,
        string subProcessorsJson = "[]"
    )
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Result<Tenant>.Failure("slug is required", "tenant.slug_required");
        }

        if (string.IsNullOrWhiteSpace(dbName))
        {
            return Result<Tenant>.Failure("db_name is required", "tenant.db_name_required");
        }

        if (string.IsNullOrWhiteSpace(region))
        {
            return Result<Tenant>.Failure("region is required", "tenant.region_required");
        }

        if (string.IsNullOrWhiteSpace(tier))
        {
            return Result<Tenant>.Failure("tier is required", "tenant.tier_required");
        }

        var tenant = new Tenant
        {
            Slug = slug.Trim().ToLowerInvariant(),
            DbName = dbName.Trim().ToLowerInvariant(),
            Region = region.Trim(),
            Tier = tier.Trim().ToLowerInvariant(),
            BusinessRegistration = string.IsNullOrWhiteSpace(businessRegistration)
                ? null
                : businessRegistration.Trim(),
            SubProcessorsJson = string.IsNullOrWhiteSpace(subProcessorsJson)
                ? "[]"
                : subProcessorsJson,
            Status = TenantStatus.Pending,
        };

        return Result<Tenant>.Success(tenant);
    }

    public Result BeginProvisioning()
    {
        if (Status is not (TenantStatus.Pending or TenantStatus.ProvisioningFailed))
        {
            return Result.Failure(
                $"cannot begin provisioning from status '{Status}'",
                "tenant.invalid_transition"
            );
        }

        Status = TenantStatus.Provisioning;
        LastFailureReason = null;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result MarkProvisioned()
    {
        if (Status != TenantStatus.Provisioning)
        {
            return Result.Failure(
                $"cannot mark provisioned from status '{Status}'",
                "tenant.invalid_transition"
            );
        }

        Status = TenantStatus.Ready;
        ProvisionedAt = DateTime.UtcNow;
        UpdatedAt = ProvisionedAt;
        return Result.Success();
    }

    public Result MarkProvisioningFailed(string reason)
    {
        if (Status != TenantStatus.Provisioning)
        {
            return Result.Failure(
                $"cannot mark provisioning_failed from status '{Status}'",
                "tenant.invalid_transition"
            );
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure("failure reason is required", "tenant.failure_reason_required");
        }

        Status = TenantStatus.ProvisioningFailed;
        LastFailureReason = reason.Trim();
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result BeginArchiving()
    {
        if (Status != TenantStatus.Ready)
        {
            return Result.Failure(
                $"cannot begin archiving from status '{Status}'",
                "tenant.invalid_transition"
            );
        }

        Status = TenantStatus.Archiving;
        ArchivingAt = DateTime.UtcNow;
        UpdatedAt = ArchivingAt;
        return Result.Success();
    }

    public Result CompleteArchiving()
    {
        if (Status != TenantStatus.Archiving)
        {
            return Result.Failure(
                $"cannot complete archiving from status '{Status}'",
                "tenant.invalid_transition"
            );
        }

        Status = TenantStatus.Archived;
        ArchivedAt = DateTime.UtcNow;
        UpdatedAt = ArchivedAt;
        return Result.Success();
    }

    public Result RecordBreachNotification()
    {
        if (BreachNotifiedAt.HasValue)
        {
            return Result.Failure(
                "breach notification already recorded",
                "tenant.breach_already_recorded"
            );
        }

        BreachNotifiedAt = DateTime.UtcNow;
        UpdatedAt = BreachNotifiedAt;
        return Result.Success();
    }
}
