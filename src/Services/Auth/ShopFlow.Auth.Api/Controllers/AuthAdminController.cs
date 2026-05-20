using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Application.Queries;
using ShopFlow.Auth.Domain;

namespace ShopFlow.Auth.Api.Controllers;

/// <summary>
/// Sprint-8 U9 — Owner-gated admin surface for tenant user CRUD
/// (R12 / R13 / R14 / R15 / R16 / F5). Standard tenant routing
/// applies (header > JWT > subdomain via
/// <c>TenantRoutingMiddleware</c>) — the caller already holds a
/// valid access token, so the JWT claim carries the tenant.
/// </summary>
/// <remarks>
/// <para>The <c>role=Owner</c> claim check is enforced via
/// <c>[Authorize(Roles = "Owner")]</c> — ASP.NET Core's authorization
/// pipeline rejects the request with 403 before this controller's
/// action body runs. Non-Owner callers (Picker, Dispatcher) see a
/// 403 ProblemDetails; unauthenticated callers see 401.</para>
/// </remarks>
[ApiController]
[Route("api/auth/admin")]
[Authorize(Roles = "Owner")]
public sealed class AuthAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthAdminController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("users")]
    [ProducesResponseType(typeof(CreateUserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateUser(
        [FromBody] CreateUserRequest? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return ValidationProblem("Request body is required.");
        }

        var result = await _mediator
            .Send(new CreateUserCommand(body.Email, body.Role), ct)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return StatusCode(StatusCodes.Status201Created, result.Value);
        }

        var status = result.ErrorCode switch
        {
            "auth.email_in_use" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return StatusCode(status, new ProblemDetails
        {
            Title = result.Error,
            Status = status,
            Type = result.ErrorCode,
        });
    }

    [HttpGet("users")]
    [ProducesResponseType(typeof(ListUsersResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator
            .Send(new ListUsersQuery(page, pageSize), ct)
            .ConfigureAwait(false);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    [HttpPut("users/{userId:guid}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetRole(
        Guid userId,
        [FromBody] UpdateUserRequest? body,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.NewRole))
        {
            return ValidationProblem("new_role is required.");
        }

        if (!TryReadTenantSlug(out var tenantSlug))
        {
            return Unauthorized();
        }

        var result = await _mediator
            .Send(new UpdateUserCommand(userId, UpdateUserOperation.SetRole, body.NewRole, tenantSlug), ct)
            .ConfigureAwait(false);

        return MapUpdateResult(result);
    }

    [HttpPost("users/{userId:guid}/reset-password")]
    [ProducesResponseType(typeof(ResetPasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(Guid userId, CancellationToken ct)
    {
        if (!TryReadTenantSlug(out var tenantSlug))
        {
            return Unauthorized();
        }

        var result = await _mediator
            .Send(new UpdateUserCommand(userId, UpdateUserOperation.ResetPassword, null, tenantSlug), ct)
            .ConfigureAwait(false);

        if (result.IsSuccess && result.Value!.ResetPassword is not null)
        {
            return Ok(result.Value.ResetPassword);
        }
        return MapUpdateResult(result);
    }

    [HttpDelete("users/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid userId, CancellationToken ct)
    {
        if (!TryReadTenantSlug(out var tenantSlug))
        {
            return Unauthorized();
        }

        var result = await _mediator
            .Send(new UpdateUserCommand(userId, UpdateUserOperation.Deactivate, null, tenantSlug), ct)
            .ConfigureAwait(false);

        return MapUpdateResult(result);
    }

    private IActionResult MapUpdateResult(ShopFlow.SharedKernel.Domain.Result<UpdateUserResult> result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }
        var status = result.ErrorCode switch
        {
            "users.not_found" => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest,
        };
        return StatusCode(status, new ProblemDetails
        {
            Title = result.Error,
            Status = status,
            Type = result.ErrorCode,
        });
    }

    private bool TryReadTenantSlug(out string slug)
    {
        slug = User.FindFirst("tenant_slug")?.Value ?? string.Empty;
        return !string.IsNullOrWhiteSpace(slug);
    }

    // ───────────── Sprint-9 admin endpoints ─────────────

    [HttpPost("users/{userId:guid}/mfa/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AdminMfaReset(Guid userId, CancellationToken ct)
    {
        if (!TryReadTenantSlug(out var slug) || !TryReadActorId(out var actorId))
        {
            return Unauthorized();
        }
        var result = await _mediator.Send(
            new AdminMfaResetCommand(actorId, userId, slug, Guid.NewGuid()),
            ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return NoContent();
        }
        var status = result.ErrorCode == "auth.mfa_required_invariant_owner"
            ? StatusCodes.Status422UnprocessableEntity
            : StatusCodes.Status404NotFound;
        return StatusCode(status, new ProblemDetails
        {
            Title = result.Error, Status = status, Type = result.ErrorCode,
        });
    }

    [HttpPost("users/{userId:guid}/unlock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AdminUnlock(Guid userId, CancellationToken ct)
    {
        if (!TryReadTenantSlug(out var slug) || !TryReadActorId(out var actorId))
        {
            return Unauthorized();
        }
        var result = await _mediator.Send(
            new AdminUnlockAccountCommand(actorId, userId, slug, Guid.NewGuid()),
            ct).ConfigureAwait(false);

        return result.IsSuccess
            ? NoContent()
            : StatusCode(StatusCodes.Status404NotFound, new ProblemDetails
            {
                Title = result.Error, Status = StatusCodes.Status404NotFound, Type = result.ErrorCode,
            });
    }

    [HttpGet("role-permissions")]
    [ProducesResponseType(typeof(IDictionary<string, IReadOnlyList<string>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRolePermissions(
        [FromServices] IRolePermissionRepository repo,
        CancellationToken ct)
    {
        var all = await repo.ListAllAsync(ct).ConfigureAwait(false);
        var wire = all.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
        return Ok(wire);
    }

    [HttpPut("role-permissions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateRolePermissions(
        [FromBody] RolePermissionsUpdateRequest? body,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Role) || string.IsNullOrWhiteSpace(body.Operation))
        {
            return ValidationProblem("role + operation are required.");
        }
        if (!Enum.TryParse<UserRole>(body.Role, ignoreCase: true, out var targetRole))
        {
            return ValidationProblem($"Unknown role '{body.Role}'.");
        }
        if (!Enum.TryParse<RolePermissionsOperation>(body.Operation, ignoreCase: true, out var op))
        {
            return ValidationProblem($"Unknown operation '{body.Operation}'.");
        }
        if (!TryReadActorId(out var actorId))
        {
            return Unauthorized();
        }

        var result = await _mediator.Send(
            new RolePermissionsCommand(actorId, targetRole, op, body.PermissionKey, body.Permissions, Guid.NewGuid()),
            ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return NoContent();
        }
        var status = result.ErrorCode switch
        {
            "auth.role_permissions_owner_critical_locked" => StatusCodes.Status422UnprocessableEntity,
            "auth.role_permissions_unknown_key" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest,
        };
        return StatusCode(status, new ProblemDetails
        {
            Title = result.Error, Status = status, Type = result.ErrorCode,
        });
    }

    private bool TryReadActorId(out Guid actorId)
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out actorId);
    }
}

public sealed record RolePermissionsUpdateRequest(
    string Role,
    string Operation,
    string? PermissionKey,
    IReadOnlyList<string>? Permissions);
