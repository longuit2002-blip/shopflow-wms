using FluentValidation;
using MediatR;

namespace ShopFlow.SharedKernel.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior: runs every registered FluentValidation
/// validator for the inbound request before delegating. Throws
/// <see cref="ValidationException"/> on failure; the API layer maps this
/// to ProblemDetails-formatted 400 via Hellang's middleware.
/// </summary>
/// <remarks>
/// Validation is a programmer-visible contract on commands/queries, not a
/// recoverable domain failure — that's why this behavior throws rather
/// than producing a <c>Result.Failure</c>. AGENTS.md §4.21 keeps
/// <see cref="Domain.Result{T}"/> for expected domain outcomes.
/// </remarks>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken
    )
    {
        if (!_validators.Any())
        {
            return await next().ConfigureAwait(false);
        }

        var context = new ValidationContext<TRequest>(request);
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        foreach (var validator in _validators)
        {
            var result = await validator
                .ValidateAsync(context, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsValid)
            {
                failures.AddRange(result.Errors);
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await next().ConfigureAwait(false);
    }
}
