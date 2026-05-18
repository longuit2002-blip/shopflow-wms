namespace ShopFlow.Auth.Infrastructure;

/// <summary>
/// Placeholder so the Auth.Infrastructure csproj has at least one type
/// and the module shape is locked into CI (Sprint-6 plan U4). Sprint-7
/// replaces with JwtTokenIssuer (Microsoft.IdentityModel.Tokens) +
/// Redis-backed denylist + EF UserConfiguration / SessionConfiguration.
/// </summary>
public static class AuthInfrastructureMarker
{
    public const string ModuleName = "Auth.Infrastructure";
}
