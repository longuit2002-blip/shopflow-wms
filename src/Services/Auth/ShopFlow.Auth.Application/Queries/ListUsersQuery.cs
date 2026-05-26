using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Queries;

/// <summary>
/// Owner-only admin paged listing query (Sprint-8 U8 / R13 / F5). The
/// authorization filter on the U9 endpoint enforces the Owner-only
/// invariant before this query reaches the handler.
/// </summary>
public sealed record ListUsersQuery(int Page, int PageSize) : IRequest<Result<ListUsersResponse>>;
