using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Application.Services;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-8 U8 — admin CreateUser handler. Generates a temporary
/// password, hashes it, persists the User aggregate, returns the
/// plaintext temporary password ONCE for the admin to relay (R12).
/// </summary>
/// <remarks>
/// <para>The plaintext temporary password is never stored or logged.
/// The U9 OTel response-body redaction filter strips it from any
/// captured response payload (KTD9). The user is expected to change
/// the password on first login via the U7
/// ChangePasswordCommandHandler.</para>
/// </remarks>
public sealed class CreateUserCommandHandler
    : IRequestHandler<CreateUserCommand, Result<CreateUserResponse>>
{
    private const string EmailInUseCode = "auth.email_in_use";
    private const string EmailInvalidCode = "users.email_invalid";
    private const string RoleInvalidCode = "users.role_invalid";

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IPasswordGenerator _generator;

    public CreateUserCommandHandler(
        IUserRepository users,
        IPasswordHasher hasher,
        IPasswordGenerator generator)
    {
        _users = users;
        _hasher = hasher;
        _generator = generator;
    }

    public async Task<Result<CreateUserResponse>> Handle(
        CreateUserCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: false, out var role)
            || !Enum.IsDefined(role))
        {
            return Result<CreateUserResponse>.Failure(
                $"Unknown role '{request.Role}'.", RoleInvalidCode);
        }

        var temporaryPassword = _generator.Generate();
        var hash = _hasher.Hash(temporaryPassword);

        User newUser;
        try
        {
            newUser = User.Create(request.Email, hash, role);
        }
        catch (ArgumentException ex) when (ex.ParamName == "email")
        {
            return Result<CreateUserResponse>.Failure(ex.Message, EmailInvalidCode);
        }

        var addResult = await _users.AddAsync(newUser, ct).ConfigureAwait(false);
        if (!addResult.IsSuccess)
        {
            return Result<CreateUserResponse>.Failure(
                addResult.Error ?? "Email already in use.",
                addResult.ErrorCode ?? EmailInUseCode);
        }

        return Result<CreateUserResponse>.Success(new CreateUserResponse(
            UserId: newUser.Id,
            Email: newUser.Email,
            Role: newUser.Role.ToString(),
            TemporaryPassword: temporaryPassword));
    }
}
