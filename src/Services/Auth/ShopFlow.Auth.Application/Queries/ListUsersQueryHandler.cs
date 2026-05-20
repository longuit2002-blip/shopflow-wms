using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Queries;

/// <summary>
/// Sprint-8 U8 — admin listing handler. Projects User aggregates to
/// the wire <see cref="UserSummary"/> shape (no PasswordHash, no
/// DomainEvents buffer — credential material never appears in
/// listings).
/// </summary>
public sealed class ListUsersQueryHandler
    : IRequestHandler<ListUsersQuery, Result<ListUsersResponse>>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    private readonly IUserRepository _users;

    public ListUsersQueryHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<Result<ListUsersResponse>> Handle(
        ListUsersQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1
            ? DefaultPageSize
            : Math.Min(request.PageSize, MaxPageSize);

        var rows = await _users.ListAsync(page, pageSize, ct).ConfigureAwait(false);
        var summaries = rows
            .Select(u => new UserSummary(
                Id: u.Id,
                Email: u.Email,
                Role: u.Role.ToString(),
                IsActive: u.IsActive,
                CreatedAt: u.CreatedAt,
                LastLoginAt: u.LastLoginAt))
            .ToList();

        return Result<ListUsersResponse>.Success(
            new ListUsersResponse(summaries, page, pageSize));
    }
}
