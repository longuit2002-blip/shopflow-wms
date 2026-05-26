using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Owner-only admin command to create a new user in the current
/// tenant (Sprint-8 U8 / R12 / F5). The server generates a temporary
/// password — admins do NOT set passwords on behalf of users; the
/// new user changes it on first login.
/// </summary>
public sealed record CreateUserCommand(string Email, string Role)
    : IRequest<Result<CreateUserResponse>>;
