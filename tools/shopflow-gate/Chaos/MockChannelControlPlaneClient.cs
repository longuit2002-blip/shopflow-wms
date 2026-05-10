using System.Net.Http.Json;
using System.Text.Json;

namespace ShopFlow.Gate.Chaos;

/// <summary>
/// Typed HTTP client for the mock-channel control plane (U7). Phase-0 doesn't
/// inject scenarios — its three checks measure cold-start, auth latency, and
/// CI time, none of which need the chaos surface. The client ships now so
/// Phase-1+ gates can reuse it without bootstrapping a parallel implementation.
///
/// The control-plane router lives at infrastructure/mock-channels/_shared/controlPlane.js
/// and is mounted under `/control` on each mock server (Shopee at :7001,
/// Lazada at :7002 per the AppHost manifest). Endpoints used here:
///
///   POST /control/scenario/:name/start    — activate a named scenario
///   POST /control/scenario/stop           — clear the active scenario
///   GET  /control/state                   — read engine + dispatcher state
///
/// All calls are idempotent (start of an already-active scenario re-arms it,
/// stop-on-stopped is a no-op), matching AGENTS.md §6 idempotency guidance
/// for cross-service control surfaces.
/// </summary>
public sealed class MockChannelControlPlaneClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public MockChannelControlPlaneClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, ownsClient: true) { }

    public MockChannelControlPlaneClient(HttpClient httpClient)
        : this(httpClient, ownsClient: false) { }

    private MockChannelControlPlaneClient(HttpClient httpClient, bool ownsClient)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsClient = ownsClient;
    }

    /// <summary>
    /// Activate the named scenario on the mock server at <paramref name="serverUrl"/>.
    /// </summary>
    /// <param name="serverUrl">Base URL of the mock server, e.g. "http://localhost:7001".</param>
    /// <param name="scenarioName">Scenario identifier as defined in the server's YAML scenarios.</param>
    public async Task StartScenarioAsync(
        string serverUrl,
        string scenarioName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioName);

        var uri = BuildUri(
            serverUrl,
            $"control/scenario/{Uri.EscapeDataString(scenarioName)}/start"
        );
        using var content = new StringContent(string.Empty);
        using var response = await _http
            .PostAsync(uri, content, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Clear the active scenario on the mock server at <paramref name="serverUrl"/>.
    /// Idempotent: stop-on-stopped is a no-op server-side.
    /// </summary>
    public async Task StopScenarioAsync(
        string serverUrl,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);

        var uri = BuildUri(serverUrl, "control/scenario/stop");
        using var content = new StringContent(string.Empty);
        using var response = await _http
            .PostAsync(uri, content, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Read the current engine + dispatcher state from the mock server. Returned as
    /// a parsed JSON document; consumers traverse the shape they care about. The
    /// server's exact payload shape is documented in
    /// infrastructure/mock-channels/_shared/scenarioEngine.js.
    /// </summary>
    public async Task<ControlPlaneState> GetStateAsync(
        string serverUrl,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);

        var uri = BuildUri(serverUrl, "control/state");
        using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response
            .Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var doc = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        string? activeScenario = null;
        if (
            doc.RootElement.TryGetProperty("active", out var activeEl)
            && activeEl.ValueKind == JsonValueKind.String
        )
        {
            activeScenario = activeEl.GetString();
        }

        return new ControlPlaneState(activeScenario, doc.RootElement.Clone());
    }

    private static Uri BuildUri(string serverUrl, string path)
    {
        var trimmed = serverUrl.TrimEnd('/');
        return new Uri($"{trimmed}/{path}");
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}

/// <summary>
/// Snapshot of a mock server's control-plane state. <see cref="ActiveScenario"/>
/// is the canonical "what's loaded right now" answer; <see cref="Raw"/> exposes
/// the full JSON payload for callers that want to inspect dispatcher counters
/// or registered webhook targets.
/// </summary>
public sealed record ControlPlaneState(string? ActiveScenario, JsonElement Raw);
