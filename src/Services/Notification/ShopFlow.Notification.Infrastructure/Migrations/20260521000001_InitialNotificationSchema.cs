using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Notification.Infrastructure;

#pragma warning disable CA1707 // EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Notification.Infrastructure.Migrations;

/// <summary>
/// Initial Notification schema per Sprint-9.5 U1 — applied per-tenant by
/// <c>shopflow-migrate apply</c> on every existing + future tenant DB.
/// Carries both <see cref="MigrationAttribute"/> and
/// <see cref="DbContextAttribute"/> per AGENTS.md §3.23; without them
/// <c>MigrateAsync()</c> is a silent no-op (see
/// <c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).
/// </summary>
/// <remarks>
/// <para>Three tables — <c>notification_outbox</c> (rendered emails
/// awaiting dispatch), <c>notification_log</c> (terminal success;
/// KTD3 idempotency UNIQUE), <c>notification_dead_letter</c> (terminal
/// failure with JSON replay payload). All three pin
/// <c>notification_kind</c> to the canonical
/// <see cref="ShopFlow.Notification.Domain.ValueObjects.NotificationKind"/>
/// string set via a CHECK constraint per the Sprint-8 U3 enum-CHECK
/// pairing precedent.</para>
/// <para>Per ADR-0003 no <c>tenant_id</c> column on any Notification
/// table; the database identity IS the tenant boundary. The migration
/// is safe to apply against any existing tenant DB — creates three
/// brand-new tables without touching pre-existing schema.</para>
/// </remarks>
[DbContext(typeof(NotificationDbContext))]
[Migration("20260521000001_InitialNotificationSchema")]
public sealed partial class InitialNotificationSchema : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);

        mb.CreateTable(
            name: "notification_outbox",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                source_event_id = table.Column<Guid>(nullable: false),
                notification_kind = table.Column<string>(maxLength: 32, nullable: false),
                recipient_email = table.Column<string>(maxLength: 254, nullable: false),
                recipient_display_name = table.Column<string>(maxLength: 256, nullable: true),
                rendered_subject = table.Column<string>(maxLength: 998, nullable: false),
                rendered_body_text = table.Column<string>(type: "text", nullable: false),
                rendered_body_html = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(
                    maxLength: 16,
                    nullable: false,
                    defaultValue: "pending"
                ),
                attempt_count = table.Column<int>(nullable: false, defaultValue: 0),
                last_attempt_at = table.Column<DateTime>(nullable: true),
                last_error_code = table.Column<string>(maxLength: 128, nullable: true),
                created_at = table.Column<DateTime>(
                    nullable: false,
                    defaultValueSql: "now() at time zone 'utc'"
                ),
                updated_at = table.Column<DateTime>(
                    nullable: false,
                    defaultValueSql: "now() at time zone 'utc'"
                ),
            },
            constraints: table => table.PrimaryKey("pk_notification_outbox", x => x.id)
        );

        mb.CreateTable(
            name: "notification_log",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                source_event_id = table.Column<Guid>(nullable: false),
                recipient_email = table.Column<string>(maxLength: 254, nullable: false),
                notification_kind = table.Column<string>(maxLength: 32, nullable: false),
                message_id = table.Column<string>(maxLength: 998, nullable: false),
                provider_response_code = table.Column<string>(maxLength: 64, nullable: true),
                sent_at = table.Column<DateTime>(nullable: false),
                created_at = table.Column<DateTime>(
                    nullable: false,
                    defaultValueSql: "now() at time zone 'utc'"
                ),
                updated_at = table.Column<DateTime>(
                    nullable: false,
                    defaultValueSql: "now() at time zone 'utc'"
                ),
            },
            constraints: table => table.PrimaryKey("pk_notification_log", x => x.id)
        );

        mb.CreateTable(
            name: "notification_dead_letter",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                source_event_id = table.Column<Guid>(nullable: false),
                recipient_email = table.Column<string>(maxLength: 254, nullable: false),
                notification_kind = table.Column<string>(maxLength: 32, nullable: false),
                payload_json = table.Column<string>(type: "jsonb", nullable: false),
                attempt_count = table.Column<int>(nullable: false),
                last_error_code = table.Column<string>(maxLength: 128, nullable: false),
                last_error_message = table.Column<string>(maxLength: 2048, nullable: true),
                dead_lettered_at = table.Column<DateTime>(nullable: false),
                created_at = table.Column<DateTime>(
                    nullable: false,
                    defaultValueSql: "now() at time zone 'utc'"
                ),
                updated_at = table.Column<DateTime>(
                    nullable: false,
                    defaultValueSql: "now() at time zone 'utc'"
                ),
            },
            constraints: table => table.PrimaryKey("pk_notification_dead_letter", x => x.id)
        );

        // Pin notification_kind across all three tables to the canonical
        // NotificationKind enum string set. Adding a 5th kind requires
        // extending these constraints via a follow-on migration AND
        // extending the C# enum + downstream consumers.
        mb.Sql(
            "ALTER TABLE notification_outbox "
                + "ADD CONSTRAINT chk_notification_outbox_kind "
                + "CHECK (notification_kind IN ('PasswordReset', 'RefreshReuse', 'AccountLocked', 'MfaEnrolled'));"
        );
        mb.Sql(
            "ALTER TABLE notification_log "
                + "ADD CONSTRAINT chk_notification_log_kind "
                + "CHECK (notification_kind IN ('PasswordReset', 'RefreshReuse', 'AccountLocked', 'MfaEnrolled'));"
        );
        mb.Sql(
            "ALTER TABLE notification_dead_letter "
                + "ADD CONSTRAINT chk_notification_dead_letter_kind "
                + "CHECK (notification_kind IN ('PasswordReset', 'RefreshReuse', 'AccountLocked', 'MfaEnrolled'));"
        );

        // Pin status to the small lifecycle set the U3 dispatcher uses.
        mb.Sql(
            "ALTER TABLE notification_outbox "
                + "ADD CONSTRAINT chk_notification_outbox_status "
                + "CHECK (status IN ('pending', 'sending'));"
        );

        // Polling index — claim oldest pending rows first (FOR UPDATE
        // SKIP LOCKED in U3 dispatcher).
        mb.CreateIndex(
            name: "ix_notification_outbox_pending",
            table: "notification_outbox",
            columns: new[] { "status", "created_at" }
        );

        // Dedup lookup index — U3 consumer may probe outbox by
        // source_event_id for diagnostic purposes; the KTD3 UNIQUE on
        // notification_log is the authoritative idempotency anchor.
        mb.CreateIndex(
            name: "ix_notification_outbox_source_event",
            table: "notification_outbox",
            column: "source_event_id"
        );

        // KTD3 idempotency anchor — duplicate MT redeliveries race a
        // second outbox row but this UNIQUE on notification_log blocks
        // the second dispatch. Npgsql SQLState 23505 caught in the
        // dispatcher; duplicate outbox row silently dropped.
        mb.CreateIndex(
            name: "ux_notification_log_source_event_recipient",
            table: "notification_log",
            columns: new[] { "source_event_id", "recipient_email" },
            unique: true
        );

        // Operator-surface index — "recent dead-letters by tenant" is
        // the inspection query operators run during incident response.
        mb.CreateIndex(
            name: "ix_notification_dead_letter_dead_lettered_at",
            table: "notification_dead_letter",
            column: "dead_lettered_at"
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);

        mb.Sql("DROP INDEX IF EXISTS ix_notification_dead_letter_dead_lettered_at;");
        mb.Sql("DROP INDEX IF EXISTS ux_notification_log_source_event_recipient;");
        mb.Sql("DROP INDEX IF EXISTS ix_notification_outbox_source_event;");
        mb.Sql("DROP INDEX IF EXISTS ix_notification_outbox_pending;");

        mb.Sql(
            "ALTER TABLE notification_outbox DROP CONSTRAINT IF EXISTS chk_notification_outbox_status;"
        );
        mb.Sql(
            "ALTER TABLE notification_dead_letter DROP CONSTRAINT IF EXISTS chk_notification_dead_letter_kind;"
        );
        mb.Sql(
            "ALTER TABLE notification_log DROP CONSTRAINT IF EXISTS chk_notification_log_kind;"
        );
        mb.Sql(
            "ALTER TABLE notification_outbox DROP CONSTRAINT IF EXISTS chk_notification_outbox_kind;"
        );

        mb.DropTable(name: "notification_dead_letter");
        mb.DropTable(name: "notification_log");
        mb.DropTable(name: "notification_outbox");
    }
}
