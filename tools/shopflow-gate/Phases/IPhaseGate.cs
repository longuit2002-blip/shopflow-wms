namespace ShopFlow.Gate.Phases;

/// <summary>
/// Plug-in seam for Phase-N gates. Each phase of the ShopFlow roadmap (0..4)
/// supplies one implementation. Program.cs's switch dispatches on the phase
/// argument to the matching IPhaseGate.
///
/// The runtime contract is intentionally tiny: a phase gate runs its checks,
/// returns a GateResult, and never throws for expected pre-conditions
/// (missing CLI tool, unreachable service, missing credentials). Those
/// surface as entries in <see cref="GateResult.Skipped"/> with an
/// actionable "needs &lt;X&gt;" message.
/// </summary>
public interface IPhaseGate
{
    /// <summary>
    /// Phase identifier — the string the user passes on the CLI ("0", "1", ...).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Run all checks for this phase. Cancellation is honored at every
    /// HTTP / process-launch boundary so a Ctrl-C from the user doesn't
    /// leave dangling subprocesses.
    /// </summary>
    Task<GateResult> RunAsync(CancellationToken cancellationToken);
}
