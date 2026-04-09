namespace LPM.Services;

/// <summary>Course type constants stored in lkp_courses.CourseType.</summary>
public static class CourseTypes
{
    public const string PC = "PC";
    public const string OT = "OT";
    public const string OTFS = "OTFS";

    /// <summary>True for OT-like types (OT, OTFS) that have instructor/CS and OT-specific behavior.</summary>
    public static bool IsOtLike(string? type) => type is OT or OTFS;
}
