namespace ShopFlow.Channel.Domain.ProductMappings;

/// <summary>
/// How a <see cref="ProductMapping"/> row was produced per Sprint-4 plan
/// R6 — the three-tier strategy from the redesign plan §9.4:
/// <list type="bullet">
///   <item><description><c>Exact</c> — string-equal lookup hit; confidence is always 1.0.</description></item>
///   <item><description><c>Fuzzy</c> — trigram/Levenshtein candidate above a configurable threshold; confidence lives in (0.5, 1.0).</description></item>
///   <item><description><c>Manual</c> — operator-supplied override; confidence is forced to 1.0 because the human is authoritative.</description></item>
/// </list>
/// </summary>
public enum MappingMethod
{
    Exact = 0,
    Fuzzy = 1,
    Manual = 2,
}
