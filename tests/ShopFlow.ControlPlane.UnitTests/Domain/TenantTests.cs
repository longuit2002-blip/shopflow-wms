using ShopFlow.ControlPlane.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.ControlPlane.UnitTests.Domain;

public class TenantTests
{
    private static Tenant NewPendingTenant(string slug = "acme")
    {
        var result = Tenant.Create(
            slug: slug,
            dbName: $"shopflow_t_{slug}",
            region: "ap-southeast-1",
            tier: "free"
        );
        result.IsSuccess.Should().BeTrue(because: result.Error);
        return result.Value!;
    }

    [Fact]
    public void Create_normalizes_slug_and_db_name_to_lowercase()
    {
        var tenant = Tenant.Create("ACME", "Shopflow_T_Acme", "ap-southeast-1", "Free").Value!;

        tenant.Slug.Should().Be("acme");
        tenant.DbName.Should().Be("shopflow_t_acme");
        tenant.Tier.Should().Be("free");
        tenant.Status.Should().Be(TenantStatus.Pending);
    }

    [Theory]
    [InlineData(null, "shopflow_t_a", "ap", "free", "tenant.slug_required")]
    [InlineData("", "shopflow_t_a", "ap", "free", "tenant.slug_required")]
    [InlineData("a", "", "ap", "free", "tenant.db_name_required")]
    [InlineData("a", "shopflow_t_a", "", "free", "tenant.region_required")]
    [InlineData("a", "shopflow_t_a", "ap", "", "tenant.tier_required")]
    public void Create_rejects_blank_fields(
        string? slug,
        string? dbName,
        string? region,
        string? tier,
        string expectedCode
    )
    {
        var result = Tenant.Create(slug!, dbName!, region!, tier!);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(expectedCode);
    }

    [Fact]
    public void BeginProvisioning_from_Pending_transitions_to_Provisioning()
    {
        var tenant = NewPendingTenant();

        var result = tenant.BeginProvisioning();

        result.IsSuccess.Should().BeTrue();
        tenant.Status.Should().Be(TenantStatus.Provisioning);
    }

    [Fact]
    public void BeginProvisioning_from_Ready_is_rejected()
    {
        var tenant = NewPendingTenant();
        tenant.BeginProvisioning();
        tenant.MarkProvisioned();

        var result = tenant.BeginProvisioning();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("tenant.invalid_transition");
    }

    [Fact]
    public void MarkProvisioned_from_Provisioning_transitions_to_Ready_and_stamps_provisioned_at()
    {
        var tenant = NewPendingTenant();
        tenant.BeginProvisioning();

        var before = DateTime.UtcNow;
        var result = tenant.MarkProvisioned();
        var after = DateTime.UtcNow;

        result.IsSuccess.Should().BeTrue();
        tenant.Status.Should().Be(TenantStatus.Ready);
        tenant.ProvisionedAt.Should().NotBeNull();
        tenant.ProvisionedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void MarkProvisioned_from_Pending_is_rejected()
    {
        var tenant = NewPendingTenant();

        var result = tenant.MarkProvisioned();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("tenant.invalid_transition");
        tenant.Status.Should().Be(TenantStatus.Pending);
    }

    [Fact]
    public void MarkProvisioningFailed_records_reason_and_allows_retry()
    {
        var tenant = NewPendingTenant();
        tenant.BeginProvisioning();

        var failed = tenant.MarkProvisioningFailed("postgres connection refused");

        failed.IsSuccess.Should().BeTrue();
        tenant.Status.Should().Be(TenantStatus.ProvisioningFailed);
        tenant.LastFailureReason.Should().Be("postgres connection refused");

        var retry = tenant.BeginProvisioning();

        retry.IsSuccess.Should().BeTrue();
        tenant.Status.Should().Be(TenantStatus.Provisioning);
        tenant.LastFailureReason.Should().BeNull();
    }

    [Fact]
    public void MarkProvisioningFailed_requires_reason()
    {
        var tenant = NewPendingTenant();
        tenant.BeginProvisioning();

        var result = tenant.MarkProvisioningFailed("   ");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("tenant.failure_reason_required");
    }

    [Fact]
    public void BeginArchiving_from_Ready_transitions_and_stamps_archiving_at()
    {
        var tenant = NewPendingTenant();
        tenant.BeginProvisioning();
        tenant.MarkProvisioned();

        var result = tenant.BeginArchiving();

        result.IsSuccess.Should().BeTrue();
        tenant.Status.Should().Be(TenantStatus.Archiving);
        tenant.ArchivingAt.Should().NotBeNull();
    }

    [Fact]
    public void BeginArchiving_from_Pending_is_rejected()
    {
        var tenant = NewPendingTenant();

        var result = tenant.BeginArchiving();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("tenant.invalid_transition");
    }

    [Fact]
    public void CompleteArchiving_from_Archiving_transitions_to_Archived()
    {
        var tenant = NewPendingTenant();
        tenant.BeginProvisioning();
        tenant.MarkProvisioned();
        tenant.BeginArchiving();

        var result = tenant.CompleteArchiving();

        result.IsSuccess.Should().BeTrue();
        tenant.Status.Should().Be(TenantStatus.Archived);
        tenant.ArchivedAt.Should().NotBeNull();
    }

    [Fact]
    public void RecordBreachNotification_is_idempotent_protected()
    {
        var tenant = NewPendingTenant();

        var first = tenant.RecordBreachNotification();
        var second = tenant.RecordBreachNotification();

        first.IsSuccess.Should().BeTrue();
        tenant.BreachNotifiedAt.Should().NotBeNull();
        second.IsSuccess.Should().BeFalse();
        second.ErrorCode.Should().Be("tenant.breach_already_recorded");
    }
}
