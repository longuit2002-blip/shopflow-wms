using ShopFlow.Outbound.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopFlowDefaults(
    builder.Configuration,
    options => options.ServiceName = "shopflow-outbound"
);

builder.Services.AddOutboundModule(builder.Configuration);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/healthz");

app.Run();

namespace ShopFlow.Outbound.Api
{
    /// <summary>
    /// Marker class for <c>WebApplicationFactory&lt;Program&gt;</c>
    /// integration tests. Phase-0 skeleton: real endpoints land in
    /// Phase-1 Sprint-3 (W5).
    /// </summary>
    public partial class Program { }
}
