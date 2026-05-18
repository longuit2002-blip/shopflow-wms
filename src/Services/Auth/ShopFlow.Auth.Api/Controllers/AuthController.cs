using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ShopFlow.Auth.Api.Controllers;

/// <summary>
/// Dev-mode fake login (Sprint-6 plan U4).
///
/// <remarks>
/// DEV-MODE STUB — Sprint-7 replaces with real JWT issuance + refresh token
/// rotation + Redis-backed denylist. DO NOT DEPLOY. The HMAC signing key
/// ships in appsettings.json marked DO-NOT-USE-IN-PROD; any non-empty
/// email + password combination succeeds. The single demo tenant slug
/// (yensaokhanhhoa) is baked into every issued token.
/// </remarks>
/// </summary>
[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthOptions options;
    private readonly JsonWebTokenHandler handler;

    public AuthController(IOptions<AuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
        this.handler = new JsonWebTokenHandler();
    }

    /// <summary>
    /// Accepts any non-empty email + password and returns a baked JWT.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest? request)
    {
        if (request is null)
        {
            return ValidationProblem("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return ValidationProblem("email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return ValidationProblem("password is required.");
        }

        var key = Encoding.UTF8.GetBytes(this.options.DevSecret);
        if (key.Length < 32)
        {
            return Problem(
                title: "Auth misconfiguration",
                detail: "Auth:DevSecret must be at least 32 bytes when UTF-8 encoded.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddSeconds(this.options.ExpiresInSeconds);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = this.options.Issuer,
            Audience = this.options.Audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expiresAt,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, request.Email),
                new Claim(JwtRegisteredClaimNames.Email, request.Email),
                new Claim("tenant_slug", this.options.DemoTenantSlug),
                new Claim("role", this.options.DemoRole),
            }),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256),
        };

        var token = this.handler.CreateToken(descriptor);

        return Ok(new LoginResponse(
            AccessToken: token,
            ExpiresIn: this.options.ExpiresInSeconds,
            TokenType: "Bearer",
            User: new LoginUser(
                Email: request.Email,
                Role: this.options.DemoRole,
                TenantSlug: this.options.DemoTenantSlug)));
    }
}

public sealed record LoginRequest(string? Email, string? Password);

public sealed record LoginResponse(
    string AccessToken,
    int ExpiresIn,
    string TokenType,
    LoginUser User);

public sealed record LoginUser(string Email, string Role, string TenantSlug);
