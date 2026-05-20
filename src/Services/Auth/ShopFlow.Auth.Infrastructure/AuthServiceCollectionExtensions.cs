using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Infrastructure.Hashing;
using ShopFlow.Auth.Infrastructure.Repositories;
using ShopFlow.Auth.Infrastructure.Storage;
using ShopFlow.Auth.Infrastructure.Tokens;
using ShopFlow.SharedKernel.Application;
using StackExchange.Redis;

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

        // Sprint-8 U4 — Argon2id password hashing. Parameters bound
        // from the Auth:Argon2 config section; OWASP 2026 baseline
        // defaults if unset. Singleton because the hasher is
        // stateless + each Hash call generates its own salt.
        services.AddOptions<Argon2Options>()
            .Bind(configuration.GetSection(Argon2Options.SectionName));
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();

        // Sprint-8 U5 — Redis-backed refresh-token store with the
        // grace-window tombstone rotation pattern (KTD3). The
        // ConnectionMultiplexer is registered as a singleton per
        // StackExchange.Redis best practice; the store layer above is
        // also singleton because Redis is its only state.
        services.AddOptions<RefreshTokenOptions>()
            .Bind(configuration.GetSection(RefreshTokenOptions.SectionName))
            .PostConfigure(opts =>
            {
                // Prefer the standard ConnectionStrings:Redis binding
                // when present (matches every other service's Redis
                // wiring); fall back to RefreshTokenOptions.ConnectionString
                // for tests that build the options directly.
                var fromConnectionStrings = configuration.GetConnectionString("Redis");
                if (!string.IsNullOrWhiteSpace(fromConnectionStrings))
                {
                    opts.ConnectionString = fromConnectionStrings;
                }
            });
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RefreshTokenOptions>>().Value;
            return ConnectionMultiplexer.Connect(opts.ConnectionString);
        });
        services.AddSingleton<IRefreshTokenStore, RedisRefreshTokenStore>();

        // Sprint-8 U6 — JWT access-token issuer. Reads iss/aud/secret
        // from the same Auth config section the kernel validator
        // (AddShopFlowDefaults) reads — single source of truth keeps
        // issuance + validation coordinated (KTD5). Singleton because
        // the handler + signing key are immutable per-process.
        services.AddOptions<JwtIssuerOptions>()
            .Bind(configuration.GetSection(JwtIssuerOptions.SectionName));
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

        return services;
    }
}
