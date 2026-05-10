using ShopFlow.Inbound.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopFlowDefaults(
    builder.Configuration,
    options => options.ServiceName = "shopflow-inbound"
);

builder.Services.AddInboundModule(builder.Configuration);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/healthz");

app.Run();

namespace ShopFlow.Inbound.Api
{
    /// <summary>
    /// Marker class for <c>WebApplicationFactory&lt;Program&gt;</c>
    /// integration tests. Phase-0 skeleton: real endpoints land in
    /// Phase-1 Sprint-2 (W4).
    /// </summary>
    public partial class Program { }
}
