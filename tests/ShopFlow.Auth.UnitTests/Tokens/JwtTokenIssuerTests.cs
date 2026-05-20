using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.Auth.Infrastructure.Tokens;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Tokens;

/// <summary>
/// Sprint-8 U6 — JwtTokenIssuer claim shape + signing + iss/aud
/// agreement with the kernel validator. The validator (in
/// SharedKernel.Infrastructure.AddShopFlowDefaults) is the source of
/// truth for what a valid token looks like; this suite pins the
/// issuer to that shape so issuance + validation can never drift
/// silently (KTD5).
/// </summary>
public sealed class JwtTokenIssuerTests
{
    // Must be >= 32 bytes UTF-8 — both the issuer and validator enforce
    // the minimum.
    private const string TestSecret = "test-secret-32-bytes-or-more-AAAAAAAAAA";
    private const string TestIssuer = "shopflow-test";
    private const string TestAudience = "shopflow-test-aud";
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    private static JwtTokenIssuer BuildIssuer(int ttlMinutes = 15) =>
        new(Options.Create(new JwtIssuerOptions
        {
            DevSecret = TestSecret,
            Issuer = TestIssuer,
            Audience = TestAudience,
            AccessTokenTtlMinutes = ttlMinutes,
        }));

    private static User BuildUser(UserRole role = UserRole.Owner) =>
        User.Create("owner@example.com", ValidHash, role);

    [Fact]
    public void IssueAccessToken_ReturnsThreeSegmentJwt()
    {
        var issuer = BuildIssuer();
        var user = BuildUser();

        var token = issuer.IssueAccessToken(user, "tenant1");

        token.Jwt.Should().NotBeNullOrWhiteSpace();
        token.Jwt.Split('.').Should().HaveCount(3, "JWT = header.payload.signature");
    }

    [Fact]
    public void IssueAccessToken_EmbedsAllCanonicalClaims()
    {
        var issuer = BuildIssuer();
        var user = BuildUser(UserRole.Picker);

        var token = issuer.IssueAccessToken(user, "yensaokhanhhoa");
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
    public void IssueAccessToken_EncodesRoleAsEnumName(UserRole role, string expected)
    {
        var issuer = BuildIssuer();
        var user = BuildUser(role);

        var token = issuer.IssueAccessToken(user, "tenant1");
        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(token.Jwt);

        jwt.GetClaim("role").Value.Should().Be(expected, "string, never the underlying int");
    }

    [Fact]
    public void IssueAccessToken_ExpiryIsIatPlusConfiguredTtl()
    {
        var issuer = BuildIssuer(ttlMinutes: 15);
        var user = BuildUser();
        var before = DateTime.UtcNow;

        var token = issuer.IssueAccessToken(user, "tenant1");

        token.ExpiresAt.Should().BeAfter(before.AddMinutes(14));
        token.ExpiresAt.Should().BeBefore(before.AddMinutes(16));
    }

    [Fact]
    public async Task IssueAccessToken_RoundTripsThroughKernelValidator()
    {
        // Pin: a token issued with secret S validates with the SAME
        // secret S and FAILS with a different secret. This is the
        // invariant the kernel JwtBearer validator depends on.
        var issuer = BuildIssuer();
        var user = BuildUser();
        var token = issuer.IssueAccessToken(user, "tenant1");

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
        var token = issuer.IssueAccessToken(user, "tenant1");

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
    public void DifferentUsers_ProduceDifferentSubClaims()
    {
        var issuer = BuildIssuer();
        var alice = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        var bob = User.Create("bob@example.com", ValidHash, UserRole.Picker);

        var ta = issuer.IssueAccessToken(alice, "tenant1");
        var tb = issuer.IssueAccessToken(bob, "tenant1");

        var handler = new JsonWebTokenHandler();
        var jwtA = handler.ReadJsonWebToken(ta.Jwt);
        var jwtB = handler.ReadJsonWebToken(tb.Jwt);

        jwtA.Subject.Should().NotBe(jwtB.Subject);
    }

    [Fact]
    public void Construct_RejectsUnderSize32ByteSecret()
    {
        var act = () => new JwtTokenIssuer(Options.Create(new JwtIssuerOptions
        {
            DevSecret = "too-short",
            Issuer = TestIssuer,
            Audience = TestAudience,
            AccessTokenTtlMinutes = 15,
        }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*32 bytes*");
    }

    [Fact]
    public void IssueAccessToken_RejectsEmptyTenantSlug()
    {
        var issuer = BuildIssuer();
        var user = BuildUser();

        var act = () => issuer.IssueAccessToken(user, "");

        act.Should().Throw<ArgumentException>();
    }
}
