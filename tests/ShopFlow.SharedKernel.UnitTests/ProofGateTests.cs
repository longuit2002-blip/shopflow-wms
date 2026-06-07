using ShopFlow.TestSupport;

namespace ShopFlow.SharedKernel.UnitTests;

/// <summary>
/// Finish-line U1 — pins the proof-run gate's pure decision logic. Lives in
/// the default unit lane (no Docker; runs in per-PR CI via
/// <c>Category!=Integration&amp;Category!=Load</c>) so a regression in the
/// gate that decides whether EVERY Docker-backed proof runs is caught cheaply
/// rather than silently disabling the whole proof suite.
/// </summary>
public sealed class ProofGateTests
{
    [Theory]
    [InlineData("1", null, true)] // local opt-in — `task proofs` sets SHOPFLOW_RUN_PROOFS=1
    [InlineData("true", null, true)] // lenient truthy form
    [InlineData(null, "true", true)] // CI auto-opt-in — GitHub Actions sets CI=true
    [InlineData("1", "true", true)] // both signals present
    [InlineData(null, null, false)] // default local dev → gated off (skip)
    [InlineData("0", "false", false)] // explicit off
    [InlineData("", "", false)] // empty strings → off
    [InlineData("yes", "1", false)] // only "1"/"true" count; CI must be "true", not "1"
    public void IsEnabled_OpensOnRunProofsOrCi(string? runProofs, string? ci, bool expected)
    {
        ProofGate.IsEnabled(runProofs, ci).Should().Be(expected);
    }

    [Fact]
    public void EnvVar_And_SkipMessage_NameTheOptInLever()
    {
        ProofGate.EnvVar.Should().Be("SHOPFLOW_RUN_PROOFS");
        ProofGate.SkipMessage.Should().Contain("SHOPFLOW_RUN_PROOFS");
    }
}
