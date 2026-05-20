using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.Auth.Infrastructure.Tokens;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Tokens;

/// <summary>
/// Sprint-8 U6 + Sprint-9 U6 — JwtTokenIssuer claim shape + signing +
/// iss/aud agreement with the kernel validator + perm-claim JSON-array
/// projection per KTD1.
/// </summary>
public sealed class JwtTokenIssuerTests
{
    private const string TestSecret = "test-secret-32-bytes-or-more-AAAAAAAAAA";
    private const string TestIssuer = "shopflow-test";
    private const string TestAudience = "shopflow-test-aud";
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    private static IRolePermissionRepository BuildRolePerms(params string[] perms)
    {
        var repo = Substitute.For<IRolePermissionRepository>();
        repo.GetForRoleAsync(Arg.Any<UserRole>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(perms.ToList()));
        return repo;
    }

    private static JwtTokenIssuer BuildIssuer(
        int ttlMinutes = 15,
        IRolePermissionRepository? rolePerms = null) =>
        new(
            Options.Create(new JwtIssuerOptions
            {
                DevSecret = TestSecret,
                Issuer = TestIssuer,
                Audience = TestAudience,
                AccessTokenTtlMinutes = ttlMinutes,
            }),
            rolePerms ?? BuildRolePerms());

    private static User BuildUser(UserRole role = UserRole.Owner) =>
        User.Create("owner@example.com", ValidHash, role);

    [Fact]
    public async Task IssueAccessToken_ReturnsThreeSegmentJwt()
    {
        var issuer = BuildIssuer();
        var user = BuildUser();

        var token = await issuer.IssueAccessTokenAsync(user, "tenant1", CancellationToken.None);

        token.Jwt.Should().NotBeNullOrWhiteSpace();
        token.Jwt.Split('.').Should().HaveCount(3, "JWT = header.payload.signature");
    }

    [Fact]
    public async Task IssueAccessToken_EmbedsAllCanonicalClaims()
    {
        var issuer = BuildIssuer();
        var user = BuildUser(UserRole.Picker);

        var token = await issuer.IssueAccessTokenAsync(user, "yensaokhanhhoa", CancellationToken.None);
        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(token.Jwt);

        jwt.Subject.Should().Be(user.Id.ToString());
        jwt.GetClaim(JwtRegisteredClaimNames.Email).Value.Should().Be("owner@example.com");
        jwt.GetClaim("role").Value.Should().Be("Picker");
        jwt.GetClaim("tenant_slug").Value.Should().Be("yensaokhanhhoa");
        jwt.Issuer.Should().Be(TestIssuer);
        jwt.Audiences.Should().Contain(TestAudience);
    }

    [Theory]
    [InlineData(UserRole.Owner, "Owner")]
    [InlineData(UserRole.Picker, "Picker")]
    [InlineData(UserRole.Dispatcher, "Dispatcher")]
    public async Task IssueAccessToken_EncodesRoleAsEnumName(UserRole role, string expected)
    {
        var issuer = BuildIssuer();
        var user = BuildUser(role);

        var token = await issuer.IssueAccessTokenAsync(user, "tenant1", CancellationToken.None);
        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(token.Jwt);

        jwt.GetClaim("role").Value.Should().Be(expected, "string, never the underlying int");
    }

    [Fact]
    public async Task IssueAccessToken_ExpiryIsIatPlusConfiguredTtl()
    {
        var issuer = BuildIssuer(ttlMinutes: 15);
        var user = BuildUser();
        var before = DateTime.UtcNow;

        var token = await issuer.IssueAccessTokenAsync(user, "tenant1", CancellationToken.None);

        token.ExpiresAt.Should().BeAfter(before.AddMinutes(14));
        token.ExpiresAt.Should().BeBefore(before.AddMinutes(16));
    }

    [Fact]
    public async Task IssueAccessToken_RoundTripsThroughKernelValidator()
    {
        var issuer = BuildIssuer();
        var user = BuildUser();
        var token = await issuer.IssueAccessTokenAsync(user, "tenant1", CancellationToken.None);

        var validParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = TestIssuer,
            ValidateAudience = true,
            ValidAudience = TestAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
        };
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token.Jwt, validParams);

        result.IsValid.Should().BeTrue(because: "issued + validated with the same secret");
    }

    [Fact]
    public async Task IssueAccessToken_FailsValidationWithDifferentSecret()
    {
        var issuer = BuildIssuer();
        var user = BuildUser();
        var token = await issuer.IssueAccessTokenAsync(user, "tenant1", CancellationToken.None);

        var wrongSecretParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = TestIssuer,
            ValidateAudience = true,
            ValidAudience = TestAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("WRONG-secret-32-bytes-or-more-AAAAAAAA")),
        };
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token.Jwt, wrongSecretParams);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task DifferentUsers_ProduceDifferentSubClaims()
    {
        var issuer = BuildIssuer();
        var alice = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        var bob = User.Create("bob@example.com", ValidHash, UserRole.Picker);

        var ta = await issuer.IssueAccessTokenAsync(alice, "tenant1", CancellationToken.None);
        var tb = await issuer.IssueAccessTokenAsync(bob, "tenant1", CancellationToken.None);

        var handler = new JsonWebTokenHandler();
        var jwtA = handler.ReadJsonWebToken(ta.Jwt);
        var jwtB = handler.ReadJsonWebToken(tb.Jwt);

        jwtA.Subject.Should().NotBe(jwtB.Subject);
    }

    [Fact]
    public void Construct_RejectsUnderSize32ByteSecret()
    {
        var act = () => new JwtTokenIssuer(
            Options.Create(new JwtIssuerOptions
            {
                DevSecret = "too-short",
                Issuer = TestIssuer,
                Audience = TestAudience,
                AccessTokenTtlMinutes = 15,
            }),
            BuildRolePerms());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*32 bytes*");
    }

    [Fact]
    public async Task IssueAccessToken_RejectsEmptyTenantSlug()
    {
        var issuer = BuildIssuer();
        var user = BuildUser();

        var act = () => issuer.IssueAccessTokenAsync(user, "", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // -------- Sprint-9 U6 perm-claim projection --------

    [Fact]
    public async Task IssueAccessToken_PermClaim_EmittedAsJsonArray()
    {
        var rolePerms = BuildRolePerms(
            "inventory.read",
            "inventory.adjust",
            "outbound.orders.read");
        var issuer = BuildIssuer(rolePerms: rolePerms);
        var user = BuildUser(UserRole.Picker);

        var token = await issuer.IssueAccessTokenAsync(user, "tenant1", CancellationToken.None);

        // Decode payload manually to assert JSON-array shape (the
        // JsonWebTokenHandler flattens N same-type claims into a JSON
        // array under the claim name).
        var payloadBase64 = token.Jwt.Split('.')[1];
        var padded = payloadBase64.PadRight(payloadBase64.Length + (4 - payloadBase64.Length % 4) % 4, '=');
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/')));
        using var doc = JsonDocument.Parse(json);
        var permEl = doc.RootElement.GetProperty("perm");
        permEl.ValueKind.Should().Be(JsonValueKind.Array, "perm claim MUST be a JSON array, not space-delimited string");
        permEl.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task IssueAccessToken_EmptyPermissionList_StillProducesValidJwt()
    {
        var issuer = BuildIssuer(rolePerms: BuildRolePerms());
        var user = BuildUser(UserRole.Picker);

        var token = await issuer.IssueAccessTokenAsync(user, "tenant1", CancellationToken.None);

        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(token.Jwt);
        jwt.Subject.Should().Be(user.Id.ToString());
        // No perm claim at all is acceptable when the role has zero grants.
    }

    [Fact]
    public async Task IssueAccessToken_RoundTripsThroughKernelValidator_AndPermClaimsSurfaceInClaimsPrincipal()
    {
        var rolePerms = BuildRolePerms(
            "auth.admin.users.list",
            "auth.admin.users.create");
        var issuer = BuildIssuer(rolePerms: rolePerms);
        var user = BuildUser(UserRole.Owner);
        var token = await issuer.IssueAccessTokenAsync(user, "tenant1", CancellationToken.None);

        var validParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = TestIssuer,
            ValidateAudience = true,
            ValidAudience = TestAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
        };
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token.Jwt, validParams);

        result.IsValid.Should().BeTrue();
        var principal = result.ClaimsIdentity!;
        var permValues = principal.FindAll("perm").Select(c => c.Value).ToHashSet();
        permValues.Should().BeEquivalentTo(new[]
        {
            "auth.admin.users.list",
            "auth.admin.users.create",
        });
    }
}
