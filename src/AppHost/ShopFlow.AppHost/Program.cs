using System;

namespace ShopFlow.AppHost;

/// <summary>
/// ShopFlow dev orchestrator entry point. Per ADR-0001, this AppHost is the
/// source-of-truth for local development only; production handoff continues
/// through the hand-maintained infrastructure/docker-compose.yml. The two
/// manifests must list the same external services — a CI smoke check
/// guards drift.
///
/// Per ADR-0002, only the Inventory module API is registered in Phase 0
/// (modular-monolith stage). The W6 mechanical split adds the remaining
/// five module API project references; resource registration here is the
/// only file that changes in that split.
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        // Infra: Postgres 16 + Redis 7 + RabbitMQ 3-management. Pinned image
        // tags per AGENTS.md rule 53 (no floating to latest, ever).
        var postgres = builder
            .AddPostgres("postgres")
            .WithImageTag("16-alpine")
            .WithDataVolume("shopflow-postgres-data")
            // Resource names: Aspire ASPIRE006 enforces ASCII letters/digits/hyphens
            // only — underscores are rejected. The Postgres DB inside the container is
            // still created as `shopflow_dev` (with underscore — Postgres convention)
            // via WithEnvironment below; only the Aspire resource handle uses hyphens.
            .AddDatabase("shopflow-dev");

        var redis = builder.AddRedis("redis").WithImageTag("7-alpine");

        var rabbitmq = builder
            .AddRabbitMQ("rabbitmq")
            .WithImageTag("3-management-alpine")
            .WithDataVolume("shopflow-rabbitmq-data");

        // Observability: Seq (logs) + Tempo (traces) + Prometheus (metrics).
        // Tempo speaks OTLP on 4317 so OTEL_EXPORTER_OTLP_ENDPOINT can point
        // straight at it.
        var seq = builder
            .AddContainer("seq", "datalust/seq", "2024.3")
            .WithEndpoint(port: 5341, targetPort: 80, name: "seq-http")
            .WithEnvironment("ACCEPT_EULA", "Y");

        var tempo = builder
            .AddContainer("tempo", "grafana/tempo", "2.6.0")
            .WithEndpoint(port: 4317, targetPort: 4317, name: "otlp-grpc")
            .WithArgs("-config.file=/etc/tempo.yaml");

        var prometheus = builder
            .AddContainer("prometheus", "prom/prometheus", "v2.55.0")
            .WithEndpoint(port: 9090, targetPort: 9090, name: "prom-http");

        // Mock-channel servers (U7). Build context is
        // infrastructure/mock-channels/, NOT the per-server folder, because
        // each Dockerfile COPYs the sibling _shared/ library. See
        // docs/solutions/2026-05-10-mock-channel-shared-library-pattern.md.
        var shopeeMock = builder
            .AddDockerfile(
                "shopee-mock",
                contextPath: "../../../infrastructure/mock-channels",
                dockerfilePath: "shopee-mock/Dockerfile"
            )
            .WithEndpoint(port: 7001, targetPort: 7001, name: "shopee-http")
            .WithEnvironment("PORT", "7001");

        var lazadaMock = builder
            .AddDockerfile(
                "lazada-mock",
                contextPath: "../../../infrastructure/mock-channels",
                dockerfilePath: "lazada-mock/Dockerfile"
            )
            .WithEndpoint(port: 7002, targetPort: 7002, name: "lazada-http")
            .WithEnvironment("PORT", "7002");

        // Inventory module API. WaitFor every external dependency so the
        // process does not race the broker / DB on cold start.
        builder
            .AddProject<Projects.ShopFlow_Inventory_Api>("inventory-api")
            .WithReference(postgres)
            .WithReference(redis)
            .WithReference(rabbitmq)
            .WaitFor(postgres)
            .WaitFor(redis)
            .WaitFor(rabbitmq)
            .WaitFor(seq)
            .WaitFor(tempo)
            .WaitFor(shopeeMock)
            .WaitFor(lazadaMock)
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://tempo:4317")
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
            .WithEnvironment("Logging__Seq__ServerUrl", "http://seq:80")
            .WithEnvironment(
                "ConnectionStrings__Inventory",
                "Host=postgres;Port=5432;Database=shopflow_dev;Username=postgres;Password=postgres"
            );

        builder.Build().Run();
    }
}
