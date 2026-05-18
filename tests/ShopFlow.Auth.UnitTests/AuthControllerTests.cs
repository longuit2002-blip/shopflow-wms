using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ShopFlow.Auth.Api.Controllers;

namespace ShopFlow.Auth.UnitTests;

/// <summary>
/// Sprint-6 plan U4 — dev-mode fake login smoke tests. Boots Auth.Api
/// in-process via WebApplicationFactory and exercises POST /auth/login.
/// </summary>
public sealed class AuthControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DevSecret = "shopflow-dev-only-do-not-use-in-prod-32bytes!!";

    private readonly WebApplicationFactory<Program> factory;

    public AuthControllerTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(b => b.UseSetting("Auth:DevSecret", DevSecret));
    }

    [Fact]
    public async Task Login_HappyPath_ReturnsJwtWithTenantSlugAndRoleClaims()
    {
        using var client = this.factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("owner@yensao.vn", "any-non-empty-password"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>();
        payload.Should().NotBeNull();
        payload!.TokenType.Should().Be("Bearer");
        payload.ExpiresIn.Should().BeGreaterThan(0);
        payload.User.Email.Should().Be("owner@yensao.vn");
        payload.User.Role.Should().Be("tenant_seller");
        payload.User.TenantSlug.Should().Be("yensaokhanhhoa");

        var handler = new JsonWebTokenHandler();
        var validation = await handler.ValidateTokenAsync(payload.AccessToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "shopflow-dev",
            ValidateAudience = true,
            ValidAudience = "shopflow-api",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DevSecret)),
        });
        validation.IsValid.Should().BeTrue(because: validation.Exception?.Message);
        validation.Claims["tenant_slug"].Should().Be("yensaokhanhhoa");
        validation.Claims["role"].Should().Be("tenant_seller");
    }

    [Fact]
    public async Task Login_EmptyEmail_Returns400ProblemDetails()
    {
        using var client = this.factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest(string.Empty, "still-required"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
