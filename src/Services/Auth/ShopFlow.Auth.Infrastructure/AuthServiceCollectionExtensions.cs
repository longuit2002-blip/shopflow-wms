using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Auth.Infrastructure;

/// <summary>
/// Composition root for the Auth module per AGENTS.md §11.79. The
/// Auth.Api project's <c>Program.cs</c> (U9) calls
/// <c>services.AddShopFlowDefaults(...)</c> first (kernel concerns) then
/// <c>services.AddAuthModule(configuration)</c> from here.
/// </summary>
/// <remarks>
/// <para>Wires:</para>
/// <list type="bullet">
///   <item><description><see cref="AuthDbContext"/> as scoped, built
///   per-request from <see cref="IRequestContext.DbConnectionString"/>.
///   The MigrationsAssembly is bound to this DLL so the AddUsers
///   migration is discovered when <c>shopflow-migrate apply</c>
///   targets the Auth context (U10).</description></item>
///   <item><description><see cref="IUserRepository"/> scoped — the
///   Argon2 hasher (U4), Redis refresh store (U5), and JWT issuer
///   (U6) register their own bindings in this same extension as they
///   land.</description></item>
/// </list>
///
/// <para>U4/U5/U6 each extend this class incrementally — each unit
/// adds its DI bindings here so the module's composition stays
/// single-file rather than fragmenting across one-extension-per-unit
/// files.</para>
/// </remarks>
public static class AuthServiceCollectionExtensions
{
    public const string ModuleName = "Auth";

    public static IServiceCollection AddAuthModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<AuthDbContext>(sp =>
        {
            var ctx = sp.GetRequiredService<IRequestContext>();
            var options = new DbContextOptionsBuilder<AuthDbContext>()
                .UseNpgsql(
                    ctx.DbConnectionString,
                    npg => npg.MigrationsAssembly(
                        typeof(AuthServiceCollectionExtensions).Assembly.GetName().Name
                    )
                )
                .Options;
            return new AuthDbContext(options);
        });

        services.AddScoped<IUserRepository, UserRepository>();

        // IPasswordHasher impl lands in U4 (Argon2idPasswordHasher).
        // IRefreshTokenStore impl lands in U5 (RedisRefreshTokenStore).
        // ITokenIssuer impl lands in U6 (JwtTokenIssuer).

        return services;
    }
}
