using Microsoft.Data.Sqlite;

namespace LPM.Services;

public record QuestionInfo(
    int QuestionId, int SessionId, string Status,
    int AskerId, string AskerName,
    int? ReplierId, string? ReplierName);

public class QuestionService
{
    private readonly string _connectionString;

    public QuestionService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>
    /// Ask a question on a session. If one already exists, resets it to Pending.
    /// </summary>
    public void AskQuestion(int sessionId, int askerId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_questions (SessionId, AskerId, Status, CreatedAt)
            VALUES (@sid, @aid, 'Pending', datetime('now'))
            ON CONFLICT(SessionId) DO UPDATE SET
                AskerId   = @aid,
                Status    = 'Pending',
                ReplierId = NULL,
                ClosedById = NULL,
                CreatedAt = datetime('now'),
                RepliedAt = NULL,
                ClosedAt  = NULL";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        cmd.Parameters.AddWithValue("@aid", askerId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Mark a session's question as replied.
    /// </summary>
    public void ReplyToQuestion(int sessionId, int replierId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_questions
            SET Status = 'Replied', ReplierId = @rid, RepliedAt = datetime('now')
            WHERE SessionId = @sid AND Status = 'Pending'";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        cmd.Parameters.AddWithValue("@rid", replierId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Close a session's question. Anyone can close.
    /// </summary>
    public void CloseQuestion(int sessionId, int closedById)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_questions
            SET Status = 'Closed', ClosedById = @cid, ClosedAt = datetime('now')
            WHERE SessionId = @sid AND Status IN ('Pending','Replied')";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        cmd.Parameters.AddWithValue("@cid", closedById);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Batch-fetch the question for each session (one per session max).
    /// </summary>
    public Dictionary<int, QuestionInfo> GetQuestionsForSessions(List<int> sessionIds)
    {
        var result = new Dictionary<int, QuestionInfo>();
        if (sessionIds.Count == 0) return result;

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();

        // Build IN clause with parameters
        var inParams = new List<string>();
        for (int i = 0; i < sessionIds.Count; i++)
        {
            var pName = $"@s{i}";
            inParams.Add(pName);
            cmd.Parameters.AddWithValue(pName, sessionIds[i]);
        }

        cmd.CommandText = $@"
            SELECT q.QuestionId, q.SessionId, q.Status,
                   q.AskerId,
                   TRIM(pa.FirstName || ' ' || COALESCE(NULLIF(pa.LastName,''), '')) AS AskerName,
                   q.ReplierId,
                   TRIM(pr.FirstName || ' ' || COALESCE(NULLIF(pr.LastName,''), '')) AS ReplierName
            FROM sess_questions q
            JOIN core_persons pa ON pa.PersonId = q.AskerId
            LEFT JOIN core_persons pr ON pr.PersonId = q.ReplierId
            WHERE q.SessionId IN ({string.Join(",", inParams)})
              AND q.Status IN ('Pending','Replied')";

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var info = new QuestionInfo(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2),
                r.GetInt32(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetInt32(5),
                r.IsDBNull(6) ? null : r.GetString(6));
            result[info.SessionId] = info;
        }
        return result;
    }

    /// <summary>
    /// Get question for a single session (for PcFolder).
    /// </summary>
    public QuestionInfo? GetQuestionForSession(int sessionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT q.QuestionId, q.SessionId, q.Status,
                   q.AskerId,
                   TRIM(pa.FirstName || ' ' || COALESCE(NULLIF(pa.LastName,''), '')) AS AskerName,
                   q.ReplierId,
                   TRIM(pr.FirstName || ' ' || COALESCE(NULLIF(pr.LastName,''), '')) AS ReplierName
            FROM sess_questions q
            JOIN core_persons pa ON pa.PersonId = q.AskerId
            LEFT JOIN core_persons pr ON pr.PersonId = q.ReplierId
            WHERE q.SessionId = @sid";
        cmd.Parameters.AddWithValue("@sid", sessionId);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new QuestionInfo(
            r.GetInt32(0), r.GetInt32(1), r.GetString(2),
            r.GetInt32(3), r.GetString(4),
            r.IsDBNull(5) ? null : r.GetInt32(5),
            r.IsDBNull(6) ? null : r.GetString(6));
    }
}
