using ShopFlow.Analytics.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopFlowDefaults(
    builder.Configuration,
    options => options.ServiceName = "shopflow-analytics"
);

builder.Services.AddAnalyticsModule(builder.Configuration);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/healthz");

app.Run();

namespace ShopFlow.Analytics.Api
{
    /// <summary>
    /// Marker class for <c>WebApplicationFactory&lt;Program&gt;</c>
    /// integration tests. Phase-0 skeleton: real endpoints land in
    /// Phase-3 Sprint-7 (W9).
    /// </summary>
    public partial class Program { }
}
