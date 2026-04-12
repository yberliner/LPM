namespace LPM.Services;

/// <summary>Course type constants stored in lkp_courses.CourseType.
/// All comparisons must go through IsAcademy / IsAdvanced — never compare raw strings.</summary>
public static class CourseTypes
{
    public const string Academy = "Academy";
    public const string Advanced = "Advanced";

    public static bool IsAcademy(string? type) =>
        string.Equals(type, Academy, System.StringComparison.OrdinalIgnoreCase);

    public static bool IsAdvanced(string? type) =>
        string.Equals(type, Advanced, System.StringComparison.OrdinalIgnoreCase);
}
