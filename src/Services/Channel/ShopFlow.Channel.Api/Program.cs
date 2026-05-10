using ShopFlow.Channel.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopFlowDefaults(
    builder.Configuration,
    options => options.ServiceName = "shopflow-channel"
);

builder.Services.AddChannelModule(builder.Configuration);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/healthz");

app.Run();

namespace ShopFlow.Channel.Api
{
    /// <summary>
    /// Marker class for <c>WebApplicationFactory&lt;Program&gt;</c>
    /// integration tests. Phase-0 skeleton: real endpoints land in
    /// Phase-2 Sprint-4/5 (W6-7).
    /// </summary>
    public partial class Program { }
}
