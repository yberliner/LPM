using Microsoft.Data.Sqlite;

namespace LPM.Services;

public record TextAnnRow(
    int Id,
    int PcId,
    string FilePath,
    string Guid,
    int PageIdx,
    string Text,
    int CreatedBy,
    string CreatedByName,
    string CreatedAt,
    int? ModifiedBy,
    string? ModifiedByName,
    string? ModifiedAt,
    bool IsDeleted,
    int? DeletedBy,
    string? DeletedByName,
    string? DeletedAt);

/// <summary>
/// Persistent audit log of text annotations added to PDF files.
/// One row per annotation guid; edits update the row, deletes soft-flag it.
/// Independent of the .ann.json sidecar / bake — the table survives bake-and-burn.
/// </summary>
public class TextAnnotationService
{
    private readonly string _connectionString;

    public TextAnnotationService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>INSERT a new annotation row, or UPDATE Text+ModifiedBy+ModifiedAt if guid already exists.</summary>
    public void Upsert(int pcId, string filePath, string guid, int pageIdx, string text, int userId)
    {
        if (string.IsNullOrEmpty(guid)) return;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Check if row exists
        using (var sel = conn.CreateCommand())
        {
            sel.CommandText = "SELECT Id, IsDeleted FROM sys_text_annotations WHERE PcId=@pc AND FilePath=@fp AND AnnotationGuid=@g LIMIT 1";
            sel.Parameters.AddWithValue("@pc", pcId);
            sel.Parameters.AddWithValue("@fp", filePath);
            sel.Parameters.AddWithValue("@g",  guid);
            using var r = sel.ExecuteReader();
            if (r.Read())
            {
                bool wasDeleted = r.GetInt32(1) == 1;
                r.Close();
                // UPDATE existing
                using var upd = conn.CreateCommand();
                upd.CommandText = wasDeleted
                    ? @"UPDATE sys_text_annotations
                        SET Text=@t, PageIdx=@pi,
                            ModifiedBy=@u, ModifiedAt=datetime('now'),
                            IsDeleted=0, DeletedBy=NULL, DeletedAt=NULL
                        WHERE PcId=@pc AND FilePath=@fp AND AnnotationGuid=@g"
                    : @"UPDATE sys_text_annotations
                        SET Text=@t, PageIdx=@pi,
                            ModifiedBy=@u, ModifiedAt=datetime('now')
                        WHERE PcId=@pc AND FilePath=@fp AND AnnotationGuid=@g";
                upd.Parameters.AddWithValue("@t",  text ?? "");
                upd.Parameters.AddWithValue("@pi", pageIdx);
                upd.Parameters.AddWithValue("@u",  userId);
                upd.Parameters.AddWithValue("@pc", pcId);
                upd.Parameters.AddWithValue("@fp", filePath);
                upd.Parameters.AddWithValue("@g",  guid);
                upd.ExecuteNonQuery();
                return;
            }
        }

        // INSERT new
        using var ins = conn.CreateCommand();
        ins.CommandText = @"
            INSERT INTO sys_text_annotations
                (PcId, FilePath, AnnotationGuid, PageIdx, Text, CreatedBy, CreatedAt)
            VALUES
                (@pc, @fp, @g, @pi, @t, @u, datetime('now'))";
        ins.Parameters.AddWithValue("@pc", pcId);
        ins.Parameters.AddWithValue("@fp", filePath);
        ins.Parameters.AddWithValue("@g",  guid);
        ins.Parameters.AddWithValue("@pi", pageIdx);
        ins.Parameters.AddWithValue("@t",  text ?? "");
        ins.Parameters.AddWithValue("@u",  userId);
        ins.ExecuteNonQuery();
    }

    public void SoftDelete(int pcId, string filePath, string guid, int userId)
    {
        if (string.IsNullOrEmpty(guid)) return;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sys_text_annotations
            SET IsDeleted=1, DeletedBy=@u, DeletedAt=datetime('now')
            WHERE PcId=@pc AND FilePath=@fp AND AnnotationGuid=@g AND IsDeleted=0";
        cmd.Parameters.AddWithValue("@u",  userId);
        cmd.Parameters.AddWithValue("@pc", pcId);
        cmd.Parameters.AddWithValue("@fp", filePath);
        cmd.Parameters.AddWithValue("@g",  guid);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Update FilePath for all rows of this PC; called from FolderService when a file is renamed/moved.</summary>
    public void RenamePath(int pcId, string oldPath, string newPath)
    {
        if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath) || oldPath == newPath) return;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sys_text_annotations SET FilePath=@new WHERE PcId=@pc AND FilePath=@old";
        cmd.Parameters.AddWithValue("@new", newPath);
        cmd.Parameters.AddWithValue("@pc",  pcId);
        cmd.Parameters.AddWithValue("@old", oldPath);
        cmd.ExecuteNonQuery();
    }

    /// <summary>List all annotations for (pcId, filePath). Active first, deleted at the end; both newest-first.</summary>
    public List<TextAnnRow> List(int pcId, string filePath)
    {
        var rows = new List<TextAnnRow>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.Id, t.PcId, t.FilePath, t.AnnotationGuid, t.PageIdx, t.Text,
                   t.CreatedBy,
                   COALESCE(TRIM(pc.FirstName || ' ' || COALESCE(NULLIF(pc.LastName,''), '')), uc.Username, '?') AS CreatedByName,
                   t.CreatedAt,
                   t.ModifiedBy,
                   CASE WHEN t.ModifiedBy IS NOT NULL
                        THEN COALESCE(TRIM(pm.FirstName || ' ' || COALESCE(NULLIF(pm.LastName,''), '')), um.Username, '?')
                        ELSE NULL END AS ModifiedByName,
                   t.ModifiedAt,
                   t.IsDeleted,
                   t.DeletedBy,
                   CASE WHEN t.DeletedBy IS NOT NULL
                        THEN COALESCE(TRIM(pd.FirstName || ' ' || COALESCE(NULLIF(pd.LastName,''), '')), ud.Username, '?')
                        ELSE NULL END AS DeletedByName,
                   t.DeletedAt
            FROM sys_text_annotations t
            LEFT JOIN core_users uc   ON uc.Id = t.CreatedBy
            LEFT JOIN core_persons pc ON pc.PersonId = uc.PersonId
            LEFT JOIN core_users um   ON um.Id = t.ModifiedBy
            LEFT JOIN core_persons pm ON pm.PersonId = um.PersonId
            LEFT JOIN core_users ud   ON ud.Id = t.DeletedBy
            LEFT JOIN core_persons pd ON pd.PersonId = ud.PersonId
            WHERE t.PcId=@pc AND t.FilePath=@fp
            ORDER BY t.IsDeleted ASC,
                     COALESCE(t.ModifiedAt, t.CreatedAt) DESC";
        cmd.Parameters.AddWithValue("@pc", pcId);
        cmd.Parameters.AddWithValue("@fp", filePath);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new TextAnnRow(
                Id:             r.GetInt32(0),
                PcId:           r.GetInt32(1),
                FilePath:       r.GetString(2),
                Guid:           r.GetString(3),
                PageIdx:        r.GetInt32(4),
                Text:           r.GetString(5),
                CreatedBy:      r.GetInt32(6),
                CreatedByName:  r.GetString(7),
                CreatedAt:      r.GetString(8),
                ModifiedBy:     r.IsDBNull(9)  ? null : r.GetInt32(9),
                ModifiedByName: r.IsDBNull(10) ? null : r.GetString(10),
                ModifiedAt:     r.IsDBNull(11) ? null : r.GetString(11),
                IsDeleted:      r.GetInt32(12) == 1,
                DeletedBy:      r.IsDBNull(13) ? null : r.GetInt32(13),
                DeletedByName:  r.IsDBNull(14) ? null : r.GetString(14),
                DeletedAt:      r.IsDBNull(15) ? null : r.GetString(15)));
        }
        return rows;
    }
}
