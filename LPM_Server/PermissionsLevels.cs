using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace LPM;

/// <summary>
/// Single source of truth for core_users.PermissionsLevel values.
/// 0 = Standard (no restrictions). Reserve 1+ for restriction profiles.
/// </summary>
public static class PermissionsLevels
{
    public const int Standard          = 0;
    public const int RestrictedAuditor = 1;
    public const int SalaryRigaOnly    = 2;

    public static bool IsRestrictedAuditor(int level) => level == RestrictedAuditor;
    public static bool IsSalaryRigaOnly(int level)    => level == SalaryRigaOnly;

    /// <summary>Reads the per-login PermissionsLevel claim. Returns 0 if missing/unparsable.</summary>
    public static int FromClaims(System.Security.Claims.ClaimsPrincipal user)
    {
        var raw = user.FindFirst("PermissionsLevel")?.Value;
        return int.TryParse(raw, out var level) ? level : Standard;
    }

    /// <summary>For gated pages: redirect restricted auditors to /Home. Returns true if redirected.</summary>
    public static async Task<bool> RedirectIfRestricted(AuthenticationStateProvider auth, NavigationManager nav)
    {
        var state = await auth.GetAuthenticationStateAsync();
        if (IsRestrictedAuditor(FromClaims(state.User)))
        {
            nav.NavigateTo("/Home", forceLoad: false);
            return true;
        }
        return false;
    }
}
