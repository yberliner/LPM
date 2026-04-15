namespace LPM;

/// <summary>
/// Single source of truth for core_users.StaffRole values.
/// </summary>
public static class StaffRoles
{
    public const string Auditor      = "Auditor";
    public const string CS           = "CS";
    public const string SeniorCS     = "SeniorCS";
    public const string AuditorAndCS = "AuditorAndCS";
    public const string Solo         = "Solo";
    public const string None         = "None";

    /// <summary>True for Auditor, CS, or SeniorCS.</summary>
    public static bool IsAuditorOrCS(string role) => role is Auditor or CS or SeniorCS;

    /// <summary>True for CS or SeniorCS.</summary>
    public static bool IsCS(string role) => role is CS or SeniorCS;

    // ── WorkCapacity helpers (sys_staff_pc_list) ──

    /// <summary>True if the WorkCapacity grants auditor access (Auditor or AuditorAndCS).</summary>
    public static bool IsAuditorCapacity(string cap) => cap is Auditor or AuditorAndCS;

    /// <summary>True if the WorkCapacity grants CS access (CS or AuditorAndCS).</summary>
    public static bool IsCsCapacity(string cap) => cap is CS or AuditorAndCS;

    /// <summary>SQL fragment for WorkCapacity that includes auditor access.</summary>
    public static string SqlInAuditorCapacity() => "('Auditor','AuditorAndCS')";

    /// <summary>SQL fragment for WorkCapacity that includes CS access.</summary>
    public static string SqlInCsCapacity() => "('CS','AuditorAndCS')";

    /// <summary>SQL fragment for StaffRole IN (CS, SeniorCS).</summary>
    public static string SqlInCS() => "('CS','SeniorCS')";

    /// <summary>SQL fragment for StaffRole IN (Auditor, CS, SeniorCS).</summary>
    public static string SqlInAuditorCS() => "('Auditor','CS','SeniorCS')";

    /// <summary>SQL fragment for StaffRole IN (Auditor, CS, SeniorCS, Solo).</summary>
    public static string SqlInAuditorCSSolo() => "('Auditor','CS','SeniorCS','Solo')";
}
