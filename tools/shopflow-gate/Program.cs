using System.Text.Json;
using ShopFlow.Gate;
using ShopFlow.Gate.Phases;

// shopflow-gate — Phase-N scale-gate orchestrator. See README.md.
//
// Hand-rolled arg parsing rather than System.CommandLine: the surface is a
// single positional phase argument plus optional flags (--json, --help). The
// 2.0 line of System.CommandLine is still in beta as of 2026-05; pulling in a
// pre-release dependency to parse one positional arg trades a real risk
// (beta API churn) for no real value.

return await RunAsync(args).ConfigureAwait(false);

static async Task<int> RunAsync(string[] args)
{
    var json = false;
    string? phase = null;

    foreach (var arg in args)
    {
        switch (arg)
        {
            case "--json":
                json = true;
                break;
            case "-h":
            case "--help":
                PrintUsage();
                return 0;
            default:
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"shopflow-gate: unknown flag `{arg}`");
                    PrintUsage();
                    return 2;
                }
                if (phase is not null)
                {
                    Console.Error.WriteLine(
                        $"shopflow-gate: positional phase already supplied (`{phase}`); got `{arg}`"
                    );
                    return 2;
                }
                phase = arg;
                break;
        }
    }

    if (phase is null)
    {
        Console.Error.WriteLine("shopflow-gate: missing required <phase> argument");
        PrintUsage();
        return 2;
    }

    IPhaseGate gate = phase switch
    {
        "0" => new PhaseZeroGate(),
        "1" or "2" or "3" or "4" => throw new NotSupportedException(
            $"Phase {phase} gate not yet implemented — Phase-0 is the only registered IPhaseGate today. See tools/shopflow-gate/README.md for the registration pattern."
        ),
        _ => throw new ArgumentOutOfRangeException(
            nameof(phase),
            phase,
            "Phase must be one of 0..4"
        ),
    };

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    GateResult result;
    try
    {
        result = await gate.RunAsync(cts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("shopflow-gate: cancelled");
        return 130;
    }

    if (json)
    {
        var payload = JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions { WriteIndented = true }
        );
        Console.WriteLine(payload);
    }
    else
    {
        PrintHumanSummary(result);
    }

    return result.Passed ? 0 : 1;
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        Usage: shopflow-gate <phase> [--json] [--help]

        Arguments:
          <phase>    Phase identifier (0..4). Today only 0 is implemented.

        Options:
          --json     Emit the result as a JSON document instead of the human summary.
          --help     Print this message and exit.

        Phase-0 runs three checks: cold-start (Aspire AppHost up to Inventory
        /healthz), auth p99 (100 sequential POSTs to /api/auth/login), and CI
        time (latest GitHub Actions run duration). Each check skips with an
        actionable message when its prerequisites are unavailable.
        """
    );
}

static void PrintHumanSummary(GateResult result)
{
    Console.WriteLine($"shopflow-gate phase {result.Phase}");
    Console.WriteLine(new string('-', 40));

    if (result.Measurements.Count > 0)
    {
        Console.WriteLine("Measurements:");
        foreach (var (key, value) in result.Measurements)
        {
            Console.WriteLine($"  {key, -22} {value:F2}");
        }
    }

    if (result.Skipped.Count > 0)
    {
        Console.WriteLine("Skipped checks:");
        foreach (var entry in result.Skipped)
        {
            Console.WriteLine($"  - {entry}");
        }
    }

    if (result.FailureReasons.Count > 0)
    {
        Console.WriteLine("Failures:");
        foreach (var entry in result.FailureReasons)
        {
            Console.WriteLine($"  - {entry}");
        }
    }

    Console.WriteLine(new string('-', 40));
    Console.WriteLine(result.Passed ? "PASSED" : "FAILED");
}
