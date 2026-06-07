using Xunit;

namespace ShopFlow.TestSupport;

/// <summary>
/// Gates the Docker-backed "hard-problem proof" suites behind an explicit
/// opt-in. A default <c>dotnet test</c> on a machine without a Docker daemon
/// skips them cleanly (Testcontainers fixtures would otherwise hard-error at
/// startup); CI and an evaluator running <c>task proofs</c> execute them.
/// </summary>
/// <remarks>
/// <para><strong>Why this exists (portfolio finish-line U1).</strong> The four
/// hard-problem proofs are Testcontainers/WebApplicationFactory tests. Before
/// this gate they were either bare <c>[Fact]</c> (so a no-Docker run errored on
/// the fixture) or a hardcoded <c>[Fact(Skip = "...CI runs it")]</c> — which a
/// <c>dotnet test</c> filter can NOT un-skip, so they ran nowhere. This gate
/// makes the skip <em>conditional</em>: opt in locally, automatic in CI.</para>
///
/// <para><strong>Enabled when</strong> <c>SHOPFLOW_RUN_PROOFS=1</c> (local
/// opt-in, set by the <c>task proofs</c> target) OR <c>CI=true</c> (GitHub
/// Actions sets this automatically on every runner). The <c>CI</c> clause
/// preserves the existing per-PR (CrossTenantRoutingTests) and nightly
/// (Category=Integration|Property) Docker runs with ZERO workflow edits — the
/// gate opens automatically wherever a CI Docker daemon is present.</para>
/// </remarks>
public static class ProofGate
{
    /// <summary>The local opt-in environment variable name.</summary>
    public const string EnvVar = "SHOPFLOW_RUN_PROOFS";

    /// <summary>
    /// Pure decision function — unit-testable without mutating process
    /// environment (env mutation in a test is global + races other tests).
    /// </summary>
    public static bool IsEnabled(string? runProofs, string? ci) =>
        string.Equals(runProofs, "1", StringComparison.Ordinal)
        || string.Equals(runProofs, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ci, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Live read of the gate against the current process environment.</summary>
    public static bool Enabled =>
        IsEnabled(
            Environment.GetEnvironmentVariable(EnvVar),
            Environment.GetEnvironmentVariable("CI")
        );

    /// <summary>The skip reason shown for gated-off proofs.</summary>
    public const string SkipMessage =
        "Hard-problem proof (Docker-backed). Set SHOPFLOW_RUN_PROOFS=1 "
        + "— e.g. `task proofs` — to run locally. Runs automatically in CI (CI=true).";

    /// <summary>
    /// Skip reason for xUnit's <c>Skip</c> property: <c>null</c> means run,
    /// non-null means skip. Read by <see cref="ProofFactAttribute"/> and
    /// <c>ProofPropertyAttribute</c> at discovery time.
    /// </summary>
    public static string? SkipReasonOrNull => Enabled ? null : SkipMessage;
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips unless <see cref="ProofGate"/> is
/// enabled. Replaces a bare <c>[Fact]</c> (or a hardcoded
/// <c>[Fact(Skip = "...")]</c>) on a hard-problem proof so the skip becomes
/// conditional on the opt-in rather than permanent. xUnit evaluates the
/// <c>Skip</c> property at discovery time, and environment variables are
/// available then, so setting it in the constructor is sufficient.
/// </summary>
public sealed class ProofFactAttribute : FactAttribute
{
    public ProofFactAttribute()
    {
        if (!ProofGate.Enabled)
        {
            Skip = ProofGate.SkipMessage;
        }
    }
}
