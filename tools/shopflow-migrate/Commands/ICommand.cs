namespace ShopFlow.Migrate.Commands;

/// <summary>
/// One subcommand handler. <see cref="Program"/> resolves the matching
/// implementation by <see cref="Name"/> and calls
/// <see cref="ExecuteAsync"/>. The return value becomes the process exit
/// code (0 success, non-zero failure — used by Aspire startup hooks and CI
/// to gate downstream steps).
/// </summary>
public interface ICommand
{
    string Name { get; }

    Task<int> ExecuteAsync(ParsedArgs args, CancellationToken ct);
}
