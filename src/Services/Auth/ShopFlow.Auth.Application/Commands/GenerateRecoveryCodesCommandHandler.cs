using System.Security.Cryptography;
using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 U8 — regenerate the 10 recovery codes for an enrolled
/// user. Deletes existing codes + mints + hashes + returns plaintexts
/// ONCE. Rejects when the user is not MFA-enrolled.
/// </summary>
public sealed class GenerateRecoveryCodesCommandHandler
    : IRequestHandler<GenerateRecoveryCodesCommand, Result<RecoveryCodeView>>
{
    private const string InvalidCredentials = "auth.invalid_credentials";
    private const string NotEnrolled = "auth.mfa_not_enrolled";
    private const int RecoveryCodeCount = 10;
    private const int RecoveryCodeLength = 10;
    private static readonly char[] Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    private readonly IUserRepository _users;
    private readonly IRecoveryCodeRepository _recoveryCodes;
    private readonly IPasswordHasher _hasher;

    public GenerateRecoveryCodesCommandHandler(
        IUserRepository users,
        IRecoveryCodeRepository recoveryCodes,
        IPasswordHasher hasher
    )
    {
        _users = users;
        _recoveryCodes = recoveryCodes;
        _hasher = hasher;
    }

    public async Task<Result<RecoveryCodeView>> Handle(
        GenerateRecoveryCodesCommand request,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _users.GetByIdAsync(request.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<RecoveryCodeView>.Failure("Invalid credentials.", InvalidCredentials);
        }
        if (!user.MfaEnrolled)
        {
            return Result<RecoveryCodeView>.Failure("MFA not enrolled.", NotEnrolled);
        }

        await _recoveryCodes.DeleteAllAsync(user.Id, ct).ConfigureAwait(false);

        var plaintexts = Enumerable
            .Range(0, RecoveryCodeCount)
            .Select(_ => GenerateCode())
            .ToList();
        var hashes = plaintexts.Select(p => _hasher.Hash(p, Argon2Profile.RecoveryCode)).ToList();
        await _recoveryCodes.AddBatchAsync(user.Id, hashes, ct).ConfigureAwait(false);

        return Result<RecoveryCodeView>.Success(new RecoveryCodeView(plaintexts, plaintexts.Count));
    }

    private static string GenerateCode()
    {
        Span<byte> buf = stackalloc byte[RecoveryCodeLength];
        RandomNumberGenerator.Fill(buf);
        Span<char> chars = stackalloc char[RecoveryCodeLength];
        for (var i = 0; i < RecoveryCodeLength; i++)
        {
            chars[i] = Alphabet[buf[i] % Alphabet.Length];
        }
        return new string(chars);
    }
}
