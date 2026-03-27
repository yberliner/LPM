using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

public record PcCompletion(int Id, int PcId, string? CompleteDate, string CreateDate, string? FinishedGrade, int? AuditorId);
public record AuditorItem(int PersonId, string FullName);
public record PcItem(int PcId, string FullName);

public class CompletionService
{
    private readonly string _connStr;

    public CompletionService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connStr = $"Data Source={dbPath}";
    }

    public List<PcCompletion> GetByPc(int pcId)
    {
        using var conn = new SqliteConnection(_connStr);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, PcId, CompleteDate, CreateDate, FinishedGrade, AuditorId
            FROM sess_completions
            WHERE PcId = @pcId
            ORDER BY CompleteDate DESC, Id DESC
            """;
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var list = new List<PcCompletion>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcCompletion(
                r.GetInt32(0),
                r.GetInt32(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetInt32(5)
            ));
        return list;
    }

    public List<AuditorItem> GetActiveAuditors()
    {
        using var conn = new SqliteConnection(_connStr);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT u.PersonId, p.FirstName || ' ' || p.LastName
            FROM core_users u
            JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE u.StaffRole IN ('Auditor', 'CS') AND u.IsActive = 1
            ORDER BY p.FirstName
            """;
        var list = new List<AuditorItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new AuditorItem(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    public List<PcItem> GetAllNonSoloPcs()
    {
        using var conn = new SqliteConnection(_connStr);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT pc.PcId, p.FirstName || ' ' || p.LastName
            FROM core_pcs pc
            JOIN core_persons p ON p.PersonId = pc.PcId
            WHERE pc.PcId NOT IN (
                SELECT u.PersonId FROM core_users u WHERE u.StaffRole = 'Solo'
            )
            ORDER BY p.FirstName
            """;
        var list = new List<PcItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcItem(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    public int Add(int pcId, string? completeDate, string? finishedGrade, int? auditorId)
    {
        using var conn = new SqliteConnection(_connStr);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sess_completions (PcId, CompleteDate, FinishedGrade, AuditorId)
            VALUES (@pcId, @completeDate, @finishedGrade, @auditorId);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@completeDate", (object?)completeDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@finishedGrade", (object?)finishedGrade ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@auditorId", (object?)auditorId ?? DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Update(int id, string? completeDate, string? finishedGrade, int? auditorId)
    {
        using var conn = new SqliteConnection(_connStr);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE sess_completions
            SET CompleteDate = @completeDate, FinishedGrade = @finishedGrade, AuditorId = @auditorId
            WHERE Id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@completeDate", (object?)completeDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@finishedGrade", (object?)finishedGrade ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@auditorId", (object?)auditorId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public record PcCompletionDetail(int Id, string? CompleteDate, string CreateDate, string? FinishedGrade, string? AuditorName);
    public record CompletionReportItem(int PcId, string PcName, string? CompleteDate, string? FinishedGrade, string? AuditorName);
    public record CompletionRow(string PcName, string? CompleteDate, string? FinishedGrade);

    public List<CompletionRow> GetForWeek(int auditorId, DateOnly from, DateOnly to)
    {
        using var conn = new SqliteConnection(_connStr);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.FirstName || ' ' || p.LastName, c.CompleteDate, c.FinishedGrade
            FROM sess_completions c
            JOIN core_persons p ON p.PersonId = c.PcId
            WHERE c.AuditorId = @auditorId
              AND c.CompleteDate >= @from
              AND c.CompleteDate <= @to
            ORDER BY c.CompleteDate
            """;
        cmd.Parameters.AddWithValue("@auditorId", auditorId);
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));
        var list = new List<CompletionRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CompletionRow(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    public List<PcCompletionDetail> GetByPcWithAuditorName(int pcId)
    {
        using var conn = new SqliteConnection(_connStr);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.Id, c.CompleteDate, c.CreateDate, c.FinishedGrade,
                   a.FirstName || ' ' || a.LastName
            FROM sess_completions c
            LEFT JOIN core_persons a ON a.PersonId = c.AuditorId
            WHERE c.PcId = @pcId
            ORDER BY c.CompleteDate DESC, c.Id DESC
            """;
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var list = new List<PcCompletionDetail>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcCompletionDetail(
                r.GetInt32(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4)
            ));
        return list;
    }

    public List<CompletionReportItem> GetAllForRange(DateOnly from, DateOnly to)
    {
        using var conn = new SqliteConnection(_connStr);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.PcId, p.FirstName || ' ' || p.LastName, c.CompleteDate, c.FinishedGrade,
                   a.FirstName || ' ' || a.LastName
            FROM sess_completions c
            JOIN core_persons p ON p.PersonId = c.PcId
            LEFT JOIN core_persons a ON a.PersonId = c.AuditorId
            WHERE c.CompleteDate >= @from AND c.CompleteDate <= @to
            ORDER BY p.FirstName, c.CompleteDate
            """;
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));
        var list = new List<CompletionReportItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CompletionReportItem(
                r.GetInt32(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4)
            ));
        return list;
    }

    public void Delete(int id)
    {
        using var conn = new SqliteConnection(_connStr);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sess_completions WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }
}
