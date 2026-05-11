namespace ShopFlow.Migrate.Provisioning;

/// <summary>
/// Encapsulates the <c>provision --tenant=&lt;slug&gt;</c> workflow per plan
/// U6 §188. Idempotent: re-running over a tenant in <c>Pending</c> or
/// <c>ProvisioningFailed</c> resumes the workflow; a tenant in <c>Ready</c>
/// is a no-op (returns <see cref="ProvisionOutcome.AlreadyReady"/>).
/// </summary>
public interface ITenantProvisioner
{
    Task<ProvisionOutcome> ProvisionAsync(string slug, CancellationToken ct);
}

public enum ProvisionOutcome
{
    /// <summary>Newly provisioned end-to-end (tenant did not exist before this call).</summary>
    Provisioned,

    /// <summary>Resumed from <c>ProvisioningFailed</c> and advanced to <c>Ready</c>.</summary>
    Resumed,

    /// <summary>Tenant was already <c>Ready</c>; the call was a no-op.</summary>
    AlreadyReady,
}
