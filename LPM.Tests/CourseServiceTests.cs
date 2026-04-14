using Microsoft.Data.Sqlite;
using LPM.Services;
using LPM.Tests.Helpers;
using Xunit;

namespace LPM.Tests;

/// <summary>
/// Tests for <see cref="CourseService"/> – course CRUD, student enrollment, finish/delete, and reports.
/// </summary>
public class CourseServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly CourseService _svc;

    public CourseServiceTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new CourseService(TestConfig.For(_dbPath));
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    // =========================================================================
    // Course CRUD
    // =========================================================================

    [Fact]
    public void AddCourse_ReturnsPositiveId()
    {
        var id = _svc.AddCourse("Communication");
        Assert.True(id > 0);
    }

    [Fact]
    public void AddCourse_MultipleCourses_EachGetUniqueId()
    {
        var id1 = _svc.AddCourse("Course A");
        var id2 = _svc.AddCourse("Course B");
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void GetAllCourses_ReturnsEmpty_WhenNoCourses()
    {
        Assert.Empty(_svc.GetAllCourses());
    }

    [Fact]
    public void GetAllCourses_ReturnsAllAddedCourses()
    {
        _svc.AddCourse("Communication");
        _svc.AddCourse("Objective Auditor");
        _svc.AddCourse("PTS/SP");

        Assert.Equal(3, _svc.GetAllCourses().Count);
    }

    [Fact]
    public void GetAllCourses_OrderedByName()
    {
        _svc.AddCourse("Zeta Course");
        _svc.AddCourse("Alpha Course");
        _svc.AddCourse("Mid Course");

        var names = _svc.GetAllCourses().Select(c => c.Name).ToList();
        Assert.Equal(names.OrderBy(n => n).ToList(), names);
    }

    [Fact]
    public void UpdateCourse_ChangesName()
    {
        var id = _svc.AddCourse("Old Name");
        _svc.UpdateCourse(id, "New Name");

        var courses = _svc.GetAllCourses();
        Assert.Contains(courses, c => c.Name == "New Name");
        Assert.DoesNotContain(courses, c => c.Name == "Old Name");
    }

    [Fact]
    public void DeleteCourse_RemovesCourse()
    {
        var id = _svc.AddCourse("Temp Course");
        _svc.DeleteCourse(id);
        Assert.Empty(_svc.GetAllCourses());
    }

    [Fact]
    public void DeleteCourse_OnlyRemovesTargetCourse()
    {
        var id1 = _svc.AddCourse("Keep This");
        var id2 = _svc.AddCourse("Delete This");
        _svc.DeleteCourse(id2);

        var courses = _svc.GetAllCourses();
        Assert.Single(courses);
        Assert.Equal("Keep This", courses[0].Name);
    }

    // =========================================================================
    // Student Course Enrollment
    // =========================================================================

    [Fact]
    public void AddStudentCourse_ReturnsPositiveId()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Alice", "A");
        var cid = _svc.AddCourse("Communication");

        var scId = _svc.AddStudentCourse(pid, cid, "2024-01-15");
        Assert.True(scId > 0);
    }

    [Fact]
    public void GetStudentCourses_ReturnsEmpty_WhenNotEnrolled()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Alice", "A");
        Assert.Empty(_svc.GetStudentCourses(pid));
    }

    [Fact]
    public void GetStudentCourses_ReturnsEnrolledCourses()
    {
        using var conn = Open();
        var pid  = TestDbHelper.InsertPerson(conn, "Alice", "A");
        var cid1 = _svc.AddCourse("Course A");
        var cid2 = _svc.AddCourse("Course B");

        _svc.AddStudentCourse(pid, cid1, "2024-01-10");
        _svc.AddStudentCourse(pid, cid2, "2024-02-10");

        var courses = _svc.GetStudentCourses(pid);
        Assert.Equal(2, courses.Count);
    }

    [Fact]
    public void GetStudentCourses_OrderedByDateDesc()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Bob", "B");
        var cid = _svc.AddCourse("Course A");

        _svc.AddStudentCourse(pid, cid, "2024-01-01");
        _svc.AddStudentCourse(pid, cid, "2024-06-01");

        var courses = _svc.GetStudentCourses(pid);
        Assert.Equal("2024-06-01", courses[0].DateStarted);
        Assert.Equal("2024-01-01", courses[1].DateStarted);
    }

    [Fact]
    public void GetStudentCourses_IncludesCourseName()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Carol", "C");
        var cid = _svc.AddCourse("Life Repair");
        _svc.AddStudentCourse(pid, cid, "2024-03-01");

        var courses = _svc.GetStudentCourses(pid);
        Assert.Equal("Life Repair", courses[0].CourseName);
    }

    [Fact]
    public void FinishStudentCourse_SetsDateFinished()
    {
        using var conn = Open();
        var pid  = TestDbHelper.InsertPerson(conn, "Dan", "D");
        var cid  = _svc.AddCourse("Course X");
        var scId = _svc.AddStudentCourse(pid, cid, "2024-01-01");

        _svc.FinishStudentCourse(scId, "2024-03-15");

        var courses = _svc.GetStudentCourses(pid);
        Assert.Equal("2024-03-15", courses[0].DateFinished);
    }

    [Fact]
    public void FinishStudentCourse_OngoingCourse_HasNullDateFinished()
    {
        using var conn = Open();
        var pid  = TestDbHelper.InsertPerson(conn, "Eve", "E");
        var cid  = _svc.AddCourse("Course Y");
        _svc.AddStudentCourse(pid, cid, "2024-01-01");

        var courses = _svc.GetStudentCourses(pid);
        Assert.Null(courses[0].DateFinished);
    }

    [Fact]
    public void DeleteStudentCourse_RemovesEnrollment()
    {
        using var conn = Open();
        var pid  = TestDbHelper.InsertPerson(conn, "Fred", "F");
        var cid  = _svc.AddCourse("Course Z");
        var scId = _svc.AddStudentCourse(pid, cid, "2024-01-01");

        _svc.DeleteStudentCourse(scId);
        Assert.Empty(_svc.GetStudentCourses(pid));
    }

    // =========================================================================
    // GetOpenCoursesForPersons
    // =========================================================================

    [Fact]
    public void GetOpenCoursesForPersons_ReturnsEmpty_ForNoIds()
    {
        Assert.Empty(_svc.GetOpenCoursesForPersons(new List<int>()));
    }

    [Fact]
    public void GetOpenCoursesForPersons_ReturnsOpenCourses_ExcludesFinished()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Alice", "A");
        var cid1 = _svc.AddCourse("Open Course");
        var cid2 = _svc.AddCourse("Done Course");

        _svc.AddStudentCourse(pid, cid1, "2024-01-01");
        var scId2 = _svc.AddStudentCourse(pid, cid2, "2024-01-01");
        _svc.FinishStudentCourse(scId2, "2024-02-01");

        var result = _svc.GetOpenCoursesForPersons(new List<int> { pid });
        Assert.True(result.ContainsKey(pid));
        Assert.Contains(result[pid], e => e.Name == "Open Course");
        Assert.DoesNotContain(result[pid], e => e.Name == "Done Course");
    }

    // =========================================================================
    // GetCourseEnrollmentReport
    // =========================================================================

    [Fact]
    public void GetCourseEnrollmentReport_ReturnsEmpty_WhenNoEnrollments()
    {
        Assert.Empty(_svc.GetCourseEnrollmentReport());
    }

    [Fact]
    public void GetCourseEnrollmentReport_ReturnsAllEnrollments()
    {
        using var conn = Open();
        var pid1 = TestDbHelper.InsertPerson(conn, "Alice", "A");
        var pid2 = TestDbHelper.InsertPerson(conn, "Bob", "B");
        var cid  = _svc.AddCourse("Communication");
        _svc.AddStudentCourse(pid1, cid, "2024-01-01");
        _svc.AddStudentCourse(pid2, cid, "2024-02-01");

        var report = _svc.GetCourseEnrollmentReport();
        Assert.Equal(2, report.Count);
    }

    // =========================================================================
    // GetVisitCountSince
    // =========================================================================

    [Fact]
    public void GetVisitCountSince_ReturnsZero_WhenNoVisits()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Alice", "A");
        Assert.Equal(0, _svc.GetVisitCountSince(pid, "2024-01-01"));
    }

    [Fact]
    public void GetVisitCountSince_CountsVisitsAfterDate()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Alice", "A");
        TestDbHelper.InsertVisit(conn, pid, "2024-01-10");
        TestDbHelper.InsertVisit(conn, pid, "2024-01-15");
        TestDbHelper.InsertVisit(conn, pid, "2024-01-20");

        Assert.Equal(2, _svc.GetVisitCountSince(pid, "2024-01-15"));
    }

    // =========================================================================
    // Schema idempotency
    // =========================================================================

    [Fact]
    public void CourseService_EnsureSchema_IsIdempotent()
    {
        var ex = Record.Exception(() =>
        {
            _ = new CourseService(TestConfig.For(_dbPath));
            _ = new CourseService(TestConfig.For(_dbPath));
        });
        Assert.Null(ex);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    // =========================================================================
    // Grades CRUD (lkp_grades)
    // =========================================================================

    [Fact]
    public void GetAllGrades_ReturnsSeeded()
    {
        var grades = _svc.GetAllGrades();
        Assert.True(grades.Count >= 3); // BA, MA, PHD seeded
    }

    [Fact]
    public void AddGrade_InsertsAndReturnsInList()
    {
        var before = _svc.GetAllGrades().Count;
        _svc.AddGrade("TestGrade");
        var after = _svc.GetAllGrades();
        Assert.Equal(before + 1, after.Count);
        Assert.Contains(after, g => g.Code == "TestGrade");
    }

    [Fact]
    public void UpdateGrade_ChangesCode()
    {
        _svc.AddGrade("OldName");
        var grade = _svc.GetAllGrades().First(g => g.Code == "OldName");
        _svc.UpdateGrade(grade.GradeId, "NewName");
        var updated = _svc.GetAllGrades();
        Assert.DoesNotContain(updated, g => g.Code == "OldName");
        Assert.Contains(updated, g => g.Code == "NewName");
    }

    [Fact]
    public void UpdateGrade_CascadesToCompletions()
    {
        _svc.AddGrade("GradeX");
        using var conn = Open();
        var pcId = TestDbHelper.InsertPerson(conn, "PC1");
        TestDbHelper.InsertPC(conn, pcId);
        TestDbHelper.Exec(conn, $"INSERT INTO sess_completions (PcId, FinishedGrade) VALUES ({pcId}, 'GradeX')");

        var grade = _svc.GetAllGrades().First(g => g.Code == "GradeX");
        _svc.UpdateGrade(grade.GradeId, "GradeY");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT FinishedGrade FROM sess_completions WHERE PcId = @id";
        cmd.Parameters.AddWithValue("@id", pcId);
        Assert.Equal("GradeY", cmd.ExecuteScalar()?.ToString());
    }

    [Fact]
    public void DeleteGrade_RemovesFromList()
    {
        _svc.AddGrade("ToDelete");
        var grade = _svc.GetAllGrades().First(g => g.Code == "ToDelete");
        _svc.DeleteGrade(grade.GradeId);
        Assert.DoesNotContain(_svc.GetAllGrades(), g => g.Code == "ToDelete");
    }

    [Fact]
    public void GetGradeUsages_DetectsAuditorReferences()
    {
        using var conn = Open();
        var gradeId = _svc.GetAllGrades().First().GradeId; // BA
        var pid = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, pid, gradeId: gradeId);

        var (auditors, completions) = _svc.GetGradeUsages(gradeId);
        Assert.NotEmpty(auditors);
    }

    [Fact]
    public void GetGradeUsages_DetectsCompletionReferences()
    {
        _svc.AddGrade("UsedGrade");
        using var conn = Open();
        var pcId = TestDbHelper.InsertPerson(conn, "PC2");
        TestDbHelper.InsertPC(conn, pcId);
        TestDbHelper.Exec(conn, $"INSERT INTO sess_completions (PcId, FinishedGrade) VALUES ({pcId}, 'UsedGrade')");

        var grade = _svc.GetAllGrades().First(g => g.Code == "UsedGrade");
        var (auditors, completions) = _svc.GetGradeUsages(grade.GradeId);
        Assert.NotEmpty(completions);
    }

    [Fact]
    public void GetGradeUsages_EmptyWhenUnused()
    {
        _svc.AddGrade("UnusedGrade");
        var grade = _svc.GetAllGrades().First(g => g.Code == "UnusedGrade");
        var (auditors, completions) = _svc.GetGradeUsages(grade.GradeId);
        Assert.Empty(auditors);
        Assert.Empty(completions);
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }
}
