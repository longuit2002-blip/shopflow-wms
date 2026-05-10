namespace ShopFlow.Gate;

/// <summary>
/// Outcome of a single phase-gate run. Designed to be both human-printable
/// (Program.cs renders a summary block) and JSON-serializable (the same
/// record shape goes to a CI artifact when --json is passed).
/// </summary>
/// <param name="Passed">
/// True when every required check succeeded. Skipped checks do NOT count
/// as failures — pre-real-execution today, several Phase-0 checks legitimately
/// skip (Aspire CLI absent, no GitHub credentials, Inventory not running).
/// </param>
/// <param name="Phase">The phase identifier this gate ran for (e.g. "0").</param>
/// <param name="FailureReasons">
/// One entry per check that ran AND failed. Empty when Passed=true.
/// </param>
/// <param name="Skipped">
/// One entry per check that did not run, with the reason. Informational; does
/// not affect Passed.
/// </param>
/// <param name="Measurements">
/// Numeric measurements collected by checks that actually executed
/// (e.g. {"coldStartSeconds": 47.2, "authP99Ms": 118.4}).
/// </param>
public sealed record GateResult(
    bool Passed,
    string Phase,
    IReadOnlyList<string> FailureReasons,
    IReadOnlyList<string> Skipped,
    IReadOnlyDictionary<string, double> Measurements
);
