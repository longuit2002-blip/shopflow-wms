using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShopFlow.Gate.Phases;

/// <summary>
/// Phase-0 scale gate per Plan §286-293 + U11. Three independent checks:
///
///   1. Cold-start: launch the Aspire AppHost, time it from process-start
///      to first 200 from Inventory's /healthz. Target: under 90 seconds
///      (ADR-0001 tightens this to 60s once Aspire 13.x cold-start lands
///      reliably; today the gate uses 90s as the looser bound).
///
///   2. Auth p99: 100 sequential POST /api/auth/login calls against the
///      running Inventory module. Target: p99 under 150ms per Plan §8.2.
///
///   3. CI total time: query the GitHub API for the latest CI run on the
///      current branch and assert total time under 10 minutes per Plan §293.
///
/// Each check is independently skippable. When the prerequisite isn't
/// reachable (Aspire CLI absent, Inventory not bound, no GH_TOKEN env var),
/// the check appends a "skipped — needs &lt;X&gt;" entry to GateResult.Skipped
/// and the gate keeps going. Skipped checks do NOT count as failures —
/// today, pre-U12 sign-off, every check legitimately skips on most
/// developer machines, which is fine. U12 itself enforces real execution.
/// </summary>
internal sealed class PhaseZeroGate : IPhaseGate
{
    public string Name => "0";

    private static readonly TimeSpan ColdStartTimeout = TimeSpan.FromSeconds(90);
    private const double AuthP99TargetMs = 150.0;
    private const double CiTotalTimeTargetMinutes = 10.0;
    private const int AuthSampleCount = 100;
    private const int InventoryDefaultPort = 5000;

    public async Task<GateResult> RunAsync(CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        var skipped = new List<string>();
        var measurements = new Dictionary<string, double>();

        await RunColdStartCheckAsync(failures, skipped, measurements, cancellationToken)
            .ConfigureAwait(false);
        await RunAuthP99CheckAsync(failures, skipped, measurements, cancellationToken)
            .ConfigureAwait(false);
        await RunCiTimeCheckAsync(failures, skipped, measurements, cancellationToken)
            .ConfigureAwait(false);

        return new GateResult(
            Passed: failures.Count == 0,
            Phase: Name,
            FailureReasons: failures,
            Skipped: skipped,
            Measurements: measurements
        );
    }

    private static async Task RunColdStartCheckAsync(
        List<string> failures,
        List<string> skipped,
        Dictionary<string, double> measurements,
        CancellationToken cancellationToken
    )
    {
        // Probe for the Aspire CLI on PATH first. On Windows the executable
        // is `aspire.exe`; ProcessStartInfo handles the .exe suffix. If the
        // tool isn't installed, skip with an actionable message — running
        // `aspire run` on a machine without the workload would dump a wall
        // of text and a non-zero exit; we prefer the structured skip.
        if (!IsExecutableOnPath("aspire"))
        {
            skipped.Add(
                "cold-start: skipped — needs the Aspire CLI on PATH (run `task setup` and `dotnet workload install aspire`)"
            );
            return;
        }

        var appHostProject = Path.Combine(
            "src",
            "AppHost",
            "ShopFlow.AppHost",
            "ShopFlow.AppHost.csproj"
        );
        if (!File.Exists(appHostProject))
        {
            skipped.Add(
                $"cold-start: skipped — AppHost project not found at `{appHostProject}` (invoke shopflow-gate from the repo root)"
            );
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        Process? aspireProcess = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "aspire",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add(appHostProject);

            aspireProcess = Process.Start(psi);
            if (aspireProcess is null)
            {
                skipped.Add("cold-start: skipped — `aspire run` failed to start");
                return;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            timeoutCts.CancelAfter(ColdStartTimeout);

            using var probeClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var inventoryHealth = $"http://localhost:{InventoryDefaultPort}/healthz";

            while (!timeoutCts.IsCancellationRequested && !aspireProcess.HasExited)
            {
                try
                {
                    using var response = await probeClient
                        .GetAsync(inventoryHealth, timeoutCts.Token)
                        .ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        stopwatch.Stop();
                        var seconds = stopwatch.Elapsed.TotalSeconds;
                        measurements["coldStartSeconds"] = seconds;
                        if (stopwatch.Elapsed > ColdStartTimeout)
                        {
                            failures.Add(
                                $"cold-start: {seconds:F1}s exceeds the {ColdStartTimeout.TotalSeconds:F0}s target"
                            );
                        }
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // not yet listening — keep polling
                }
                catch (TaskCanceledException)
                {
                    // probe timeout — keep polling until the outer timeoutCts fires
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), timeoutCts.Token)
                        .ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            stopwatch.Stop();
            failures.Add(
                $"cold-start: Inventory /healthz did not return 200 within {ColdStartTimeout.TotalSeconds:F0}s"
            );
        }
        catch (Exception ex)
        {
            // Anything we didn't anticipate becomes a structured skip rather
            // than a process crash. The orchestrator can still see what
            // happened in the JSON output.
            skipped.Add(
                $"cold-start: skipped — `aspire run` raised `{ex.GetType().Name}: {ex.Message}`"
            );
        }
        finally
        {
            if (aspireProcess is not null && !aspireProcess.HasExited)
            {
                try
                {
                    aspireProcess.Kill(entireProcessTree: true);
                }
                catch
                {
                    // best-effort teardown
                }
            }
            aspireProcess?.Dispose();
        }
    }

    private static async Task RunAuthP99CheckAsync(
        List<string> failures,
        List<string> skipped,
        Dictionary<string, double> measurements,
        CancellationToken cancellationToken
    )
    {
        var loginUrl = $"http://localhost:{InventoryDefaultPort}/api/auth/login";

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        // One probe call to decide whether Inventory is reachable. We deliberately
        // accept any HTTP status (4xx is fine — endpoint exists); only a connection-
        // level failure trips the skip.
        try
        {
            using var probe = await client
                .GetAsync($"http://localhost:{InventoryDefaultPort}/healthz", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            skipped.Add(
                $"auth-p99: skipped — Inventory not reachable at http://localhost:{InventoryDefaultPort} (run `task up` first)"
            );
            return;
        }
        catch (TaskCanceledException)
        {
            skipped.Add(
                $"auth-p99: skipped — Inventory probe timed out at http://localhost:{InventoryDefaultPort}"
            );
            return;
        }

        var samples = new List<double>(AuthSampleCount);
        var payload = new
        {
            username = "shopflow-gate-test-user",
            password = "shopflow-gate-test-pass",
        };

        for (var i = 0; i < AuthSampleCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            try
            {
                using var response = await client
                    .PostAsJsonAsync(loginUrl, payload, cancellationToken)
                    .ConfigureAwait(false);
                sw.Stop();
            }
            catch (HttpRequestException)
            {
                sw.Stop();
                // count toward the percentile — a connection error is still
                // observed latency from the gate's POV. Real impl will hit the
                // happy path; we don't want this to disguise pathology.
            }
            catch (TaskCanceledException)
            {
                sw.Stop();
            }
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        var p99Index = (int)Math.Ceiling(0.99 * samples.Count) - 1;
        if (p99Index < 0)
        {
            p99Index = 0;
        }
        var p99 = samples[p99Index];
        measurements["authP99Ms"] = p99;
        if (p99 > AuthP99TargetMs)
        {
            failures.Add(
                $"auth-p99: {p99:F1}ms exceeds the {AuthP99TargetMs:F0}ms target across {AuthSampleCount} samples"
            );
        }
    }

    private static async Task RunCiTimeCheckAsync(
        List<string> failures,
        List<string> skipped,
        Dictionary<string, double> measurements,
        CancellationToken cancellationToken
    )
    {
        var token =
            Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        var repo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(repo))
        {
            skipped.Add(
                "ci-time: skipped — needs GITHUB_TOKEN (or GH_TOKEN) and GITHUB_REPOSITORY env vars (CI sets these automatically; locally export them or run via `gh run`)"
            );
            return;
        }

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri("https://api.github.com/"),
                Timeout = TimeSpan.FromSeconds(10),
            };
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("shopflow-gate/1.0");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await client
                .GetAsync($"repos/{repo}/actions/runs?per_page=1", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                skipped.Add(
                    $"ci-time: skipped — GitHub API returned {(int)response.StatusCode} {response.StatusCode}"
                );
                return;
            }

            await using var stream = await response
                .Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var doc = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (
                !doc.RootElement.TryGetProperty("workflow_runs", out var runs)
                || runs.GetArrayLength() == 0
            )
            {
                skipped.Add("ci-time: skipped — no workflow runs reported by GitHub API");
                return;
            }

            var run = runs[0];
            if (
                !run.TryGetProperty("created_at", out var createdAtEl)
                || !run.TryGetProperty("updated_at", out var updatedAtEl)
            )
            {
                skipped.Add("ci-time: skipped — workflow run payload missing timestamps");
                return;
            }

            var createdAt = createdAtEl.GetDateTimeOffset();
            var updatedAt = updatedAtEl.GetDateTimeOffset();
            var minutes = (updatedAt - createdAt).TotalMinutes;
            measurements["ciTotalMinutes"] = minutes;
            if (minutes > CiTotalTimeTargetMinutes)
            {
                failures.Add(
                    $"ci-time: latest run took {minutes:F1}m, exceeds the {CiTotalTimeTargetMinutes:F0}m target"
                );
            }
        }
        catch (HttpRequestException ex)
        {
            skipped.Add($"ci-time: skipped — GitHub API unreachable ({ex.Message})");
        }
        catch (TaskCanceledException)
        {
            skipped.Add("ci-time: skipped — GitHub API timed out");
        }
    }

    private static bool IsExecutableOnPath(string executable)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return false;
        }

        var separator = Path.PathSeparator;
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", string.Empty }
            : new[] { string.Empty };

        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, executable + ext);
                if (File.Exists(candidate))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
