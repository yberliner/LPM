namespace LPM;

/// <summary>
/// Single source of truth for core_users.StaffRole values.
/// </summary>
public static class StaffRoles
{
    public const string Auditor            = "Auditor";
    public const string CS                 = "CS";
    public const string SeniorCS           = "SeniorCS";
    public const string AuditorAndCS       = "AuditorAndCS";
    public const string CSReview           = "CSReview";
    public const string CSSolo             = "CSSolo";
    public const string AuditorAndCSReview = "AuditorAndCSReview";
    public const string AuditorAndCSSolo   = "AuditorAndCSSolo";
    public const string Solo               = "Solo";
    public const string None               = "None";

    /// <summary>True for Auditor, CS, or SeniorCS.</summary>
    public static bool IsAuditorOrCS(string role) => role is Auditor or CS or SeniorCS;

    /// <summary>True for CS or SeniorCS.</summary>
    public static bool IsCS(string role) => role is CS or SeniorCS;

    // ── WorkCapacity helpers (sys_staff_pc_list) ──

    /// <summary>True if the WorkCapacity grants auditor access.</summary>
    public static bool IsAuditorCapacity(string cap) =>
        cap is Auditor or AuditorAndCS or AuditorAndCSReview or AuditorAndCSSolo;

    /// <summary>True if the WorkCapacity grants any CS access (all sessions, review-only, or solo-only).</summary>
    public static bool IsCsCapacity(string cap) =>
        cap is CS or AuditorAndCS or CSReview or CSSolo or AuditorAndCSReview or AuditorAndCSSolo;

    /// <summary>True if the WorkCapacity is specifically for solo CS sessions only.</summary>
    public static bool IsCsSoloCapacity(string cap) => cap is CSSolo or AuditorAndCSSolo;

    /// <summary>True if the WorkCapacity is specifically for non-solo CS sessions only.</summary>
    public static bool IsCsReviewCapacity(string cap) => cap is CSReview or AuditorAndCSReview;

    /// <summary>SQL fragment for WorkCapacity that includes auditor access.</summary>
    public static string SqlInAuditorCapacity() =>
        "('Auditor','AuditorAndCS','AuditorAndCSReview','AuditorAndCSSolo')";

    /// <summary>SQL fragment for WorkCapacity that includes any CS access.</summary>
    public static string SqlInCsCapacity() =>
        "('CS','AuditorAndCS','CSReview','CSSolo','AuditorAndCSReview','AuditorAndCSSolo')";

    /// <summary>SQL fragment for StaffRole IN (CS, SeniorCS).</summary>
    public static string SqlInCS() => "('CS','SeniorCS')";

    /// <summary>SQL fragment for StaffRole IN (Auditor, CS, SeniorCS).</summary>
    public static string SqlInAuditorCS() => "('Auditor','CS','SeniorCS')";

    /// <summary>SQL fragment for StaffRole IN (Auditor, CS, SeniorCS, Solo).</summary>
    public static string SqlInAuditorCSSolo() => "('Auditor','CS','SeniorCS','Solo')";
}
