using Microsoft.Data.Sqlite;

namespace LPM.Services;

public record MeetingItem(
    int MeetingId, int PcId, string PcName,
    int? AuditorId, string? AuditorName,
    string MeetingType, DateTime StartAt, int LengthSeconds,
    bool IsWeekly, DateTime CreatedAt, int CreatedBy);

public class MeetingService(IConfiguration config)
{
    private readonly string _connectionString =
        $"Data Source={config["Database:Path"] ?? "lifepower.db"}";

    public List<MeetingItem> GetMeetings(DateTime from, DateTime to, string meetingType)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // Fetch non-recurring meetings in range + all recurring meetings that started before range end
        cmd.CommandText = @"
            SELECT m.MeetingId, m.PcId,
                   TRIM(pp.FirstName || ' ' || COALESCE(NULLIF(pp.LastName,''), '')) AS PcName,
                   m.AuditorId,
                   CASE WHEN m.AuditorId IS NOT NULL
                        THEN TRIM(pa.FirstName || ' ' || COALESCE(NULLIF(pa.LastName,''), ''))
                        ELSE NULL END AS AuditorName,
                   m.MeetingType, m.StartAt, m.LengthSeconds, m.IsWeekly, m.CreatedAt, m.CreatedBy
            FROM sess_meetings m
            JOIN core_persons pp ON pp.PersonId = m.PcId
            LEFT JOIN core_persons pa ON pa.PersonId = m.AuditorId
            WHERE m.MeetingType = @type
              AND (
                    (m.IsWeekly = 0 AND m.StartAt >= @from AND m.StartAt < @to)
                 OR (m.IsWeekly = 1 AND m.StartAt < @to)
              )
            ORDER BY m.StartAt";
        cmd.Parameters.AddWithValue("@type", meetingType);
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));

        var raw = new List<MeetingItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            raw.Add(new MeetingItem(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetInt32(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetString(5),
                DateTime.Parse(r.GetString(6)),
                r.GetInt32(7),
                r.GetInt32(8) != 0,
                DateTime.Parse(r.GetString(9)),
                r.GetInt32(10)));
        }

        // Expand recurring meetings into individual occurrences within [from, to)
        var result = new List<MeetingItem>();
        foreach (var m in raw)
        {
            if (!m.IsWeekly)
            {
                result.Add(m);
            }
            else
            {
                var occurrence = m.StartAt;
                // Advance to first occurrence on or after 'from'
                if (occurrence < from)
                {
                    var days = (int)Math.Ceiling((from - occurrence).TotalDays);
                    var weeks = (days + 6) / 7;
                    occurrence = occurrence.AddDays(weeks * 7);
                }
                while (occurrence < to)
                {
                    result.Add(m with { StartAt = occurrence });
                    occurrence = occurrence.AddDays(7);
                }
            }
        }

        return result.OrderBy(m => m.StartAt).ToList();
    }

    public int AddMeeting(int pcId, int? auditorId, string meetingType,
        DateTime startAt, int lengthSeconds, bool isWeekly, int createdBy)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_meetings
                (PcId, AuditorId, MeetingType, StartAt, LengthSeconds, IsWeekly, CreatedAt, CreatedBy)
            VALUES (@pcId, @auditorId, @type, @startAt, @len, @weekly, datetime('now'), @createdBy);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@auditorId", (object?)auditorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type", meetingType);
        cmd.Parameters.AddWithValue("@startAt", startAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@len", lengthSeconds);
        cmd.Parameters.AddWithValue("@weekly", isWeekly ? 1 : 0);
        cmd.Parameters.AddWithValue("@createdBy", createdBy);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void UpdateMeeting(int meetingId, int pcId, int? auditorId, string meetingType,
        DateTime startAt, int lengthSeconds, bool isWeekly)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_meetings
            SET PcId=@pcId, AuditorId=@auditorId, MeetingType=@type,
                StartAt=@startAt, LengthSeconds=@len, IsWeekly=@weekly
            WHERE MeetingId=@id";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@auditorId", (object?)auditorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type", meetingType);
        cmd.Parameters.AddWithValue("@startAt", startAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@len", lengthSeconds);
        cmd.Parameters.AddWithValue("@weekly", isWeekly ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", meetingId);
        cmd.ExecuteNonQuery();
    }

    public MeetingItem? GetMeetingById(int meetingId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT m.MeetingId, m.PcId,
                   TRIM(pp.FirstName || ' ' || COALESCE(NULLIF(pp.LastName,''), '')) AS PcName,
                   m.AuditorId,
                   CASE WHEN m.AuditorId IS NOT NULL
                        THEN TRIM(pa.FirstName || ' ' || COALESCE(NULLIF(pa.LastName,''), ''))
                        ELSE NULL END AS AuditorName,
                   m.MeetingType, m.StartAt, m.LengthSeconds, m.IsWeekly, m.CreatedAt, m.CreatedBy
            FROM sess_meetings m
            JOIN core_persons pp ON pp.PersonId = m.PcId
            LEFT JOIN core_persons pa ON pa.PersonId = m.AuditorId
            WHERE m.MeetingId = @id";
        cmd.Parameters.AddWithValue("@id", meetingId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new MeetingItem(
            r.GetInt32(0), r.GetInt32(1), r.GetString(2),
            r.IsDBNull(3) ? null : r.GetInt32(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.GetString(5),
            DateTime.Parse(r.GetString(6)),
            r.GetInt32(7),
            r.GetInt32(8) != 0,
            DateTime.Parse(r.GetString(9)),
            r.GetInt32(10));
    }

    public void DeleteMeeting(int meetingId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sess_meetings WHERE MeetingId = @id";
        cmd.Parameters.AddWithValue("@id", meetingId);
        cmd.ExecuteNonQuery();
    }
}
